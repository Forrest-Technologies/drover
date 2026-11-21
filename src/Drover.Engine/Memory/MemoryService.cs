namespace Drover.Engine.Memory;

/// <summary>
/// Who is writing a note: the run's bot, for the run's person, from one tool call in one conversation, and whether
/// that run has read text from an untrusted source.
/// </summary>
public sealed record NoteWriter(Guid BotId, Guid UserId, Guid? ThreadId, Guid RunId, Guid ToolCallId, bool Tainted = false);


/// <summary>How long one note may be, and how much the bot's notes about one person may hold together.</summary>
public sealed record NoteLimits(int NoteCharacters, int NotesCharacters);


/// <summary>Why a person's change to their notes was not made.</summary>
public enum NoteProblem
{
    None,
    NotFound,
    Invalid,
    Changed,
}


/// <summary>
/// What became of a person's change to their notes: the note as it now stands (or as it stood, once removed), or why
/// nothing changed. <see cref="Detail"/> says why in a sentence the person can act on.
/// </summary>
public sealed record NoteOutcome(MemoryNote? Note, NoteProblem Problem = NoteProblem.None, string? Detail = null)
{
    public static NoteOutcome NotFound { get; } = new(null, NoteProblem.NotFound);

    public static NoteOutcome Invalid(string detail) => new(null, NoteProblem.Invalid, detail);

    public static NoteOutcome Changed { get; } = new(null, NoteProblem.Changed, "This note has changed since.");
}


/// <summary>
/// Where a note a bot wrote came from: the message in the conversation, and whether that conversation is the main
/// one. A person sees it only for a conversation they can still open.
/// </summary>
public sealed record NoteOrigin(Guid ThreadId, Guid MessageId, bool IsMain);


/// <summary>
/// What a person sees of a bot's notes about them: whether memory is on for the bot, every note newest first with
/// where it came from, and whether the notes are nearly as large as they may grow.
/// </summary>
public sealed record PersonMemory(
    Bot Bot,
    bool Enabled,
    IReadOnlyList<MemoryNote> Notes,
    IReadOnlyDictionary<Guid, NoteOrigin> Origins,
    bool NearlyFull);


/// <summary>
/// The notes a bot keeps about the person it works for. A bot reads and changes only its own notes about the person
/// its run acts for, and only while memory is on for it and the conversation is private to that person: a note is
/// the person's own, so it is never written from, or read into, a conversation other people can see. Every refusal is
/// a sentence the bot can act on. Runs inside the caller's tenant transaction.
/// </summary>
public sealed class MemoryService(
    MemoryRepository memories,
    BotRepository bots,
    ThreadRepository threads,
    MessageRepository messages,
    PolicyService policies,
    TimeProvider time)
{
    #region Public Methods

    /// <summary>The limits a bot's notes obey: its own settings, never above the server's.</summary>
    public NoteLimits LimitsFor(MemorySettings settings)
    {
        var deployment = policies.Deployment;
        return new(
            Math.Min(settings.MaxNoteCharacters ?? deployment.MaxMemoryNoteCharacters, deployment.MaxMemoryNoteCharacters),
            Math.Min(settings.MaxNotesCharacters ?? deployment.MaxMemoryCharactersPerPerson, deployment.MaxMemoryCharactersPerPerson));
    }

    /// <summary>
    /// What a run is given of the bot's notes about its person, or null when it is given nothing: memory is off for
    /// the bot, or the conversation is not private to that person.
    /// </summary>
    public async Task<RecalledMemory?> Recall(Bot bot, ChatThread thread, Guid userId, CancellationToken cancellationToken)
    {
        if (!bot.MemorySettings.Enabled || thread.AudienceUserId != userId)
        {
            return null;
        }

        var notes = await memories.ListNotes(bot.Id, userId, cancellationToken);
        return new RecalledMemory(
            [.. notes.Select(note => new RecalledNote(note.Id, note.Text, note.UpdatedAt))],
            LimitsFor(bot.MemorySettings).NotesCharacters);
    }

    public async Task<(IReadOnlyList<MemoryNote> Notes, NoteLimits Limits)> List(NoteWriter writer, CancellationToken cancellationToken)
    {
        var limits = await Open(writer, cancellationToken);
        return (await memories.ListNotes(writer.BotId, writer.UserId, cancellationToken), limits);
    }

    public async Task<(MemoryNote Note, int Used, NoteLimits Limits)> Save(NoteWriter writer, string text, CancellationToken cancellationToken)
    {
        var limits = await Open(writer, cancellationToken);
        RefuseLongNote(text, limits);
        await memories.LockNotes(writer.BotId, writer.UserId, cancellationToken);
        var used = Used(await memories.ListNotes(writer.BotId, writer.UserId, cancellationToken));
        RefuseFullNotes(used, text.Length, limits);

        var now = time.GetUtcNow();
        var note = new MemoryNote
        {
            BotId = writer.BotId,
            AudienceUserId = writer.UserId,
            Text = text,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await WrittenBy(note, writer, cancellationToken);
        memories.Add(note);
        return (note, used + text.Length, limits);
    }

    /// <summary>Rewrites one of the bot's notes. <c>Before</c> is the text it replaced.</summary>
    public async Task<(MemoryNote Note, string Before, int Used, NoteLimits Limits)> Change(
        NoteWriter writer,
        Guid noteId,
        string text,
        CancellationToken cancellationToken)
    {
        var limits = await Open(writer, cancellationToken);
        RefuseLongNote(text, limits);
        await memories.LockNotes(writer.BotId, writer.UserId, cancellationToken);
        var note = await FindNote(writer, noteId, cancellationToken);
        var others = Used(await memories.ListNotes(writer.BotId, writer.UserId, cancellationToken)) - note.Text.Length;
        RefuseFullNotes(others, text.Length, limits);

        var before = note.Text;
        note.Text = text;
        note.UpdatedAt = time.GetUtcNow();
        await WrittenBy(note, writer, cancellationToken);
        return (note, before, others + text.Length, limits);
    }

    public async Task<MemoryNote> Forget(NoteWriter writer, Guid noteId, CancellationToken cancellationToken)
    {
        await Open(writer, cancellationToken);
        await memories.LockNotes(writer.BotId, writer.UserId, cancellationToken);
        var note = await FindNote(writer, noteId, cancellationToken);
        memories.Remove(note);
        return note;
    }

    #endregion

    #region A Person's Own Notes

    /// <summary>
    /// The bot's notes about the person, newest first, or null when there is no such bot. They are listed whether or
    /// not memory is on, so a person can always see and remove what is kept about them.
    /// </summary>
    public async Task<PersonMemory?> ListForPerson(Guid botId, Guid userId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { } bot)
        {
            return null;
        }

        var notes = await memories.ListNotes(botId, userId, cancellationToken);
        var limits = LimitsFor(bot.MemorySettings);
        return new(
            bot,
            bot.MemorySettings.Enabled,
            [.. notes.OrderByDescending(note => note.UpdatedAt).ThenByDescending(note => note.Id)],
            await OriginsOf(notes, userId, cancellationToken),
            IsNearlyFull(Used(notes), limits));
    }

    /// <summary>A note the person writes themselves. It is theirs, so it is never tainted.</summary>
    public async Task<NoteOutcome> AddByPerson(Guid botId, Guid userId, string text, CancellationToken cancellationToken)
    {
        text = NoteText.Flatten(text);
        var opened = await OpenForPerson(botId, text, cancellationToken);
        if (opened.Limits is not { } limits)
        {
            return opened.Refused ?? NoteOutcome.NotFound;
        }

        await memories.LockNotes(botId, userId, cancellationToken);
        var used = Used(await memories.ListNotes(botId, userId, cancellationToken));
        if (TooFullFor(used, text.Length, limits) is { } full)
        {
            return NoteOutcome.Invalid(full);
        }

        var now = time.GetUtcNow();
        var note = new MemoryNote
        {
            BotId = botId,
            AudienceUserId = userId,
            Text = text,
            WrittenByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        memories.Add(note);
        return new(note);
    }

    /// <summary>
    /// The person rewrites a note. A clean note becomes theirs. A tainted one stays tainted, and keeps its bot writer
    /// and the moment it came from, whatever the person writes: no edit is proof that nothing the tainted run read is
    /// left. A person who wants a clean note removes it and adds their own.
    /// </summary>
    public async Task<NoteOutcome> ChangeByPerson(Guid botId, Guid userId, Guid noteId, string text, CancellationToken cancellationToken)
    {
        text = NoteText.Flatten(text);
        var opened = await OpenForPerson(botId, text, cancellationToken);
        if (opened.Limits is not { } limits)
        {
            return opened.Refused ?? NoteOutcome.NotFound;
        }

        await memories.LockNotes(botId, userId, cancellationToken);
        if (await memories.GetNote(noteId, botId, userId, cancellationToken) is not { } note)
        {
            return NoteOutcome.NotFound;
        }

        var others = Used(await memories.ListNotes(botId, userId, cancellationToken)) - note.Text.Length;
        if (TooFullFor(others, text.Length, limits) is { } full)
        {
            return NoteOutcome.Invalid(full);
        }

        note.Text = text;
        note.UpdatedAt = time.GetUtcNow();
        if (!note.Tainted)
        {
            note.WrittenByUserId = userId;
            note.WrittenByBotId = null;
            note.SourceThreadId = null;
            note.SourceMessageId = null;
        }

        return new(note);
    }

    /// <summary>
    /// Removes the note at once, memory on or off, and returns it as it was so it can be put back exactly. Given
    /// <paramref name="expectedText"/>, as from a card that shows the note as it was saved, a note whose text has
    /// changed since is left as it is: the person asked to remove what they saw, not what it has become.
    /// </summary>
    public async Task<NoteOutcome> RemoveByPerson(
        Guid botId,
        Guid userId,
        Guid noteId,
        string? expectedText,
        CancellationToken cancellationToken)
    {
        await memories.LockNotes(botId, userId, cancellationToken);
        if (await memories.GetNote(noteId, botId, userId, cancellationToken) is not { } note)
        {
            return NoteOutcome.NotFound;
        }

        if (expectedText is not null && !string.Equals(note.Text, NoteText.Flatten(expectedText), StringComparison.Ordinal))
        {
            return NoteOutcome.Changed;
        }

        memories.Remove(note);
        return new(note);
    }

    /// <summary>
    /// Puts back a note the person removed, exactly as it was: its text, who wrote it, where it came from, and whether
    /// it is tainted. <paramref name="removed"/> must be the server's own record of the removal, never a client's
    /// account of it. A note already back is left as it is.
    /// </summary>
    public async Task<NoteOutcome> RestoreByPerson(Guid botId, Guid userId, MemoryNote removed, CancellationToken cancellationToken)
    {
        if (removed.BotId != botId || removed.AudienceUserId != userId || await bots.FindBot(botId, cancellationToken) is not { } bot)
        {
            return NoteOutcome.NotFound;
        }

        await memories.LockNotes(botId, userId, cancellationToken);
        if (await memories.GetNote(removed.Id, botId, userId, cancellationToken) is { } already)
        {
            return new(already);
        }

        var limits = LimitsFor(bot.MemorySettings);
        if (removed.Text.Length > limits.NoteCharacters)
        {
            return NoteOutcome.Invalid(
                $"A note may now hold at most {limits.NoteCharacters} characters, and this one has {removed.Text.Length}, so it cannot be put back.");
        }

        var used = Used(await memories.ListNotes(botId, userId, cancellationToken));
        if (TooFullFor(used, removed.Text.Length, limits) is { } full)
        {
            return NoteOutcome.Invalid(full);
        }

        memories.Add(removed);
        return new(removed);
    }

    #endregion

    #region Checks

    /// <summary>
    /// The bot's limits, once it is clear the bot may keep notes here: it exists, memory is on for it, and the
    /// conversation is private to the person the run acts for.
    /// </summary>
    private async Task<NoteLimits> Open(NoteWriter writer, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(writer.BotId, cancellationToken) is not { } bot)
        {
            throw new BuiltinToolException("This bot no longer exists.");
        }

        if (!bot.MemorySettings.Enabled)
        {
            throw new BuiltinToolException("Memory is off for this bot, so it keeps no notes. A person can turn it on in the bot's Memory settings.");
        }

        if (writer.ThreadId is not { } threadId
            || await threads.FindThread(threadId, cancellationToken) is not { } thread
            || thread.AudienceUserId != writer.UserId)
        {
            throw new BuiltinToolException("Notes are kept only from a private conversation with the person they are about. This conversation is not one.");
        }

        return LimitsFor(bot.MemorySettings);
    }

    private static void RefuseLongNote(string text, NoteLimits limits)
    {
        if (text.Length == 0)
        {
            throw new BuiltinToolException("The argument text is required: the note, in a sentence or two.");
        }

        if (text.Length > limits.NoteCharacters)
        {
            throw new BuiltinToolException(
                $"This note is {text.Length} characters long, and a note may hold at most {limits.NoteCharacters}. Keep one fact to a note, in fewer words, and save again.");
        }
    }

    private static void RefuseFullNotes(int used, int adding, NoteLimits limits)
    {
        if (used + adding > limits.NotesCharacters)
        {
            throw new BuiltinToolException(
                $"Your notes about this person hold {used} of the {limits.NotesCharacters} characters they may hold, and this note needs {adding}. Nothing was saved. Merge notes that say related things with memory_change, or remove notes that no longer hold with memory_forget, then try again.");
        }
    }

    /// <summary>
    /// The bot's limits, once it is clear the person may write a note to it: the bot exists, memory is on for it, and
    /// the text is a note it may hold. Otherwise the limits are null and the problem says why.
    /// </summary>
    private async Task<(NoteLimits? Limits, NoteOutcome? Refused)> OpenForPerson(Guid botId, string text, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { } bot)
        {
            return (null, NoteOutcome.NotFound);
        }

        if (!bot.MemorySettings.Enabled)
        {
            return (null, NoteOutcome.Invalid($"Memory is off for {bot.Name}, so it keeps no notes."));
        }

        var limits = LimitsFor(bot.MemorySettings);
        return text.Length switch
        {
            0 => (null, NoteOutcome.Invalid("Write the note first.")),
            var length when length > limits.NoteCharacters => (null, NoteOutcome.Invalid(
                $"A note may hold at most {limits.NoteCharacters} characters, and this one has {length}. Keep one fact to a note, in fewer words.")),
            _ => (limits, null),
        };
    }

    private static string? TooFullFor(int used, int adding, NoteLimits limits) =>
        used + adding > limits.NotesCharacters
            ? $"The notes hold {used} of the {limits.NotesCharacters} characters they may hold, and this note needs {adding}. Shorten or remove a note first."
            : null;

    private async Task<MemoryNote> FindNote(NoteWriter writer, Guid noteId, CancellationToken cancellationToken) =>
        await memories.GetNote(noteId, writer.BotId, writer.UserId, cancellationToken)
            ?? throw new BuiltinToolException($"There is no note with the id {noteId} among your notes about this person. memory_list shows them.");

    #endregion

    #region Private Methods

    private static int Used(IReadOnlyList<MemoryNote> notes) => notes.Sum(note => note.Text.Length);

    /// <summary>Nearly full is nine tenths of what the notes may hold: from there a new note may not fit.</summary>
    private static bool IsNearlyFull(int used, NoteLimits limits) => used * 10L >= limits.NotesCharacters * 9L;

    /// <summary>
    /// Where each note a bot wrote came from, for the conversations the person can still open. Row-level security
    /// hides a conversation that is no longer theirs alone, so its notes simply have no origin.
    /// </summary>
    private async Task<Dictionary<Guid, NoteOrigin>> OriginsOf(IReadOnlyList<MemoryNote> notes, Guid userId, CancellationToken cancellationToken)
    {
        Dictionary<Guid, NoteOrigin> origins = [];
        foreach (var group in notes.GroupBy(note => note.SourceThreadId))
        {
            if (group.Key is not { } threadId
                || await threads.FindThread(threadId, cancellationToken) is not { ArchivedAt: null } thread
                || thread.AudienceUserId != userId)
            {
                continue;
            }

            foreach (var note in group.Where(note => note.SourceMessageId is not null))
            {
                origins[note.Id] = new(threadId, note.SourceMessageId.GetValueOrDefault(), thread.IsMain);
            }
        }

        return origins;
    }

    /// <summary>
    /// The bot wrote the note now, in this conversation, in the reply that made this call. A note a tainted run wrote
    /// stays tainted, whoever writes it again.
    /// </summary>
    private async Task WrittenBy(MemoryNote note, NoteWriter writer, CancellationToken cancellationToken)
    {
        var source = writer.ThreadId is { } threadId
            ? await messages.FindReplyWithCall(threadId, writer.RunId, writer.ToolCallId, cancellationToken)
            : null;
        note.WrittenByBotId = writer.BotId;
        note.WrittenByUserId = null;
        note.Tainted = note.Tainted || writer.Tainted;
        note.SourceThreadId = source?.ThreadId;
        note.SourceMessageId = source?.Id;
    }

    #endregion
}
