namespace Drover.Server.Bots;

/// <summary>
/// The signed-in person's own notes for a bot: read, add, rewrite, remove, and put back. Under the tenant scope
/// filter, where row-level security keeps every other person's notes out of reach, so another person's note is simply
/// not found. Any member may keep notes with any bot they can see.
/// </summary>
public static class MemoryEndpoints
{
    #region Private Fields

    private const string UndoPurpose = "Drover.Memory.UndoRemove";

    // Long enough to reach the toast's Undo, short enough that a token is not a lasting copy of the note.
    private static readonly TimeSpan undoLifetime = TimeSpan.FromMinutes(10);

    #endregion

    #region Public Methods

    public static RouteGroupBuilder MapMemoryEndpoints(this RouteGroupBuilder bots)
    {
        bots.MapGet("/{botId:guid}/memory", ListNotes)
            .WithName("ListMemoryNotes")
            .WithSummary("The caller's own notes with the bot, newest first, and whether memory is on for it.");
        bots.MapPost("/{botId:guid}/memory/notes", AddNote).WithName("AddMemoryNote");
        bots.MapPut("/{botId:guid}/memory/notes/{noteId:guid}", ChangeNote).WithName("ChangeMemoryNote");
        bots.MapDelete("/{botId:guid}/memory/notes/{noteId:guid}", RemoveNote)
            .WithName("RemoveMemoryNote")
            .WithSummary("Removes the note at once and returns a token that puts it back exactly, for ten minutes.");
        bots.MapPost("/{botId:guid}/memory/notes/restore", RestoreNote).WithName("RestoreMemoryNote");
        return bots;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<BotMemoryResponse>, NotFound>> ListNotes(Guid botId, HttpContext context, MemoryService memory) =>
        await memory.ListForPerson(botId, context.GetSpaceAccess().UserId, context.RequestAborted) is { } found
            ? TypedResults.Ok(new BotMemoryResponse(found.Enabled, found.NearlyFull, [.. found.Notes.Select(note => note.ToResponse(found.Origins))]))
            : TypedResults.NotFound();

    private static async Task<Results<Created<MemoryNoteResponse>, NotFound, ProblemHttpResult>> AddNote(
        Guid botId,
        WriteNoteRequest request,
        HttpContext context,
        MemoryService memory)
    {
        var access = context.GetSpaceAccess();
        var outcome = await memory.AddByPerson(botId, access.UserId, request.Text ?? string.Empty, context.RequestAborted);
        return outcome switch
        {
            { Note: { } note } => TypedResults.Created($"/api/spaces/{access.SpaceId}/bots/{botId}/memory/notes/{note.Id}", note.ToResponse()),
            { Problem: NoteProblem.NotFound } => TypedResults.NotFound(),
            _ => Refused(outcome),
        };
    }

    private static async Task<Results<Ok<MemoryNoteResponse>, NotFound, ProblemHttpResult>> ChangeNote(
        Guid botId,
        Guid noteId,
        WriteNoteRequest request,
        HttpContext context,
        MemoryService memory)
    {
        var outcome = await memory.ChangeByPerson(botId, context.GetSpaceAccess().UserId, noteId, request.Text ?? string.Empty, context.RequestAborted);
        return outcome switch
        {
            { Note: { } note } => TypedResults.Ok(note.ToResponse()),
            { Problem: NoteProblem.NotFound } => TypedResults.NotFound(),
            _ => Refused(outcome),
        };
    }

    private static async Task<Results<Ok<RemovedNoteResponse>, NotFound>> RemoveNote(
        Guid botId,
        Guid noteId,
        HttpContext context,
        MemoryService memory,
        IDataProtectionProvider protection)
    {
        var access = context.GetSpaceAccess();
        var outcome = await memory.RemoveByPerson(botId, access.UserId, noteId, context.RequestAborted);
        return outcome.Note is { } removed
            ? TypedResults.Ok(new RemovedNoteResponse(removed.Id, Seal(protection, access, removed)))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<MemoryNoteResponse>, NotFound, ProblemHttpResult>> RestoreNote(
        Guid botId,
        RestoreNoteRequest request,
        HttpContext context,
        MemoryService memory,
        IDataProtectionProvider protection,
        ILoggerFactory logs)
    {
        var access = context.GetSpaceAccess();
        if (Unseal(protection, access, request.UndoToken, logs) is not { } removed)
        {
            return TypedResults.Problem(
                title: "This note can no longer be put back. Undo lasts ten minutes; write the note again to keep it.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await memory.RestoreByPerson(botId, access.UserId, removed, context.RequestAborted);
        return outcome switch
        {
            { Note: { } note } => TypedResults.Ok(note.ToResponse()),
            { Problem: NoteProblem.NotFound } => TypedResults.NotFound(),
            _ => Refused(outcome),
        };
    }

    #endregion

    #region Undo Token

    /// <summary>
    /// The removed note, sealed for the person who removed it in the space they removed it from. It carries who wrote
    /// the note and whether it is tainted, so a client must not be able to change it: it is encrypted and signed.
    /// </summary>
    private static string Seal(IDataProtectionProvider protection, SpaceAccess access, MemoryNote note) =>
        Protector(protection, access).Protect(
            JsonSerializer.Serialize(new SealedNote(
                note.SpaceId,
                note.AudienceUserId,
                note.BotId,
                note.Id,
                note.Text,
                note.WrittenByBotId,
                note.WrittenByUserId,
                note.SourceThreadId,
                note.SourceMessageId,
                note.Tainted,
                note.CreatedAt,
                note.UpdatedAt)),
            undoLifetime);

    /// <summary>The note a token seals, or null when it is not one this server sealed for this person here, or it has expired.</summary>
    private static MemoryNote? Unseal(IDataProtectionProvider protection, SpaceAccess access, string? token, ILoggerFactory logs)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var sealedNote = JsonSerializer.Deserialize<SealedNote>(Protector(protection, access).Unprotect(token));
            return sealedNote is { } note && note.SpaceId == access.SpaceId && note.AudienceUserId == access.UserId
                ? note.ToNote()
                : null;
        }
        catch (CryptographicException exception)
        {
            // An expired, changed, or foreign token puts nothing back.
            logs.CreateLogger(typeof(MemoryEndpoints).FullName!).LogInformation(exception, "An undo token for a removed note was refused");
            return null;
        }
    }

    // The purpose names the person and the space, so a token sealed for one never opens for another.
    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider protection, SpaceAccess access) =>
        protection.CreateProtector(UndoPurpose, access.SpaceId.ToString(), access.UserId.ToString()).ToTimeLimitedDataProtector();

    #endregion

    #region Private Methods

    private static ProblemHttpResult Refused(NoteOutcome outcome) =>
        TypedResults.Problem(title: outcome.Detail, statusCode: StatusCodes.Status400BadRequest);

    private static MemoryNoteResponse ToResponse(this MemoryNote note, IReadOnlyDictionary<Guid, NoteOrigin>? origins = null) =>
        new(
            note.Id,
            note.Text,
            note.WrittenByBotId is null ? "person" : "bot",
            note.UpdatedAt,
            origins?.GetValueOrDefault(note.Id) is { } origin ? new(origin.ThreadId, origin.MessageId, origin.IsMain) : null);

    #endregion

    #region Nested Types

    private sealed record SealedNote(
        Guid SpaceId,
        Guid AudienceUserId,
        Guid BotId,
        Guid Id,
        string Text,
        Guid? WrittenByBotId,
        Guid? WrittenByUserId,
        Guid? SourceThreadId,
        Guid? SourceMessageId,
        bool Tainted,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
    {
        public MemoryNote ToNote() =>
            new()
            {
                SpaceId = SpaceId,
                AudienceUserId = AudienceUserId,
                BotId = BotId,
                Id = Id,
                Text = Text,
                WrittenByBotId = WrittenByBotId,
                WrittenByUserId = WrittenByUserId,
                SourceThreadId = SourceThreadId,
                SourceMessageId = SourceMessageId,
                Tainted = Tainted,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
            };
    }

    #endregion
}
