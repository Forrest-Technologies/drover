namespace Drover.Engine.Memory;

/// <summary>Who is writing a note: the run's bot, for the run's person, from one tool call in one conversation.</summary>
public sealed record NoteWriter(Guid BotId, Guid UserId, Guid? ThreadId, Guid RunId, Guid ToolCallId);


/// <summary>How long one note may be, and how much the bot's notes about one person may hold together.</summary>
public sealed record NoteLimits(int NoteCharacters, int NotesCharacters);


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

    public async Task<(MemoryNote Note, int Used, NoteLimits Limits)> Change(
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

        note.Text = text;
        note.UpdatedAt = time.GetUtcNow();
        await WrittenBy(note, writer, cancellationToken);
        return (note, others + text.Length, limits);
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

    private async Task<MemoryNote> FindNote(NoteWriter writer, Guid noteId, CancellationToken cancellationToken) =>
        await memories.GetNote(noteId, writer.BotId, writer.UserId, cancellationToken)
            ?? throw new BuiltinToolException($"There is no note with the id {noteId} among your notes about this person. memory_list shows them.");

    #endregion

    #region Private Methods

    private static int Used(IReadOnlyList<MemoryNote> notes) => notes.Sum(note => note.Text.Length);

    /// <summary>The bot wrote the note now, in this conversation, in the reply that made this call.</summary>
    private async Task WrittenBy(MemoryNote note, NoteWriter writer, CancellationToken cancellationToken)
    {
        var source = writer.ThreadId is { } threadId
            ? await messages.FindReplyWithCall(threadId, writer.RunId, writer.ToolCallId, cancellationToken)
            : null;
        note.WrittenByBotId = writer.BotId;
        note.WrittenByUserId = null;
        note.SourceThreadId = source?.ThreadId;
        note.SourceMessageId = source?.Id;
    }

    #endregion
}
