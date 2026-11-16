namespace Drover.Client;

/// <summary>
/// The signed-in person's own notes with a bot. The server reaches no one else's: another person's note is not found.
/// </summary>
public sealed partial class DroverApi
{
    #region Notes

    public Task<BotMemoryResponse> GetBotMemory(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<BotMemoryResponse>(Notes(spaceId, botId), cancellationToken);

    public Task<MemoryNoteResponse> AddMemoryNote(Guid spaceId, Guid botId, string text, CancellationToken cancellationToken = default) =>
        Post<MemoryNoteResponse>($"{Notes(spaceId, botId)}/notes", new WriteNoteRequest(text), cancellationToken);

    public Task<MemoryNoteResponse> ChangeMemoryNote(Guid spaceId, Guid botId, Guid noteId, string text, CancellationToken cancellationToken = default) =>
        Send<MemoryNoteResponse>(HttpMethod.Put, $"{Notes(spaceId, botId)}/notes/{noteId}", new WriteNoteRequest(text), cancellationToken);

    /// <summary>Removes the note. The response carries the token that puts it back with <see cref="RestoreMemoryNote"/>.</summary>
    public Task<RemovedNoteResponse> RemoveMemoryNote(Guid spaceId, Guid botId, Guid noteId, CancellationToken cancellationToken = default) =>
        Send<RemovedNoteResponse>(HttpMethod.Delete, $"{Notes(spaceId, botId)}/notes/{noteId}", null, cancellationToken);

    /// <summary>Removes the note only while it still says <paramref name="text"/>; the server answers 409 once it has changed.</summary>
    public Task<RemovedNoteResponse> ForgetMemoryNote(Guid spaceId, Guid botId, Guid noteId, string text, CancellationToken cancellationToken = default) =>
        Post<RemovedNoteResponse>($"{Notes(spaceId, botId)}/notes/{noteId}/forget", new ForgetNoteRequest(text), cancellationToken);

    public Task<MemoryNoteResponse> RestoreMemoryNote(Guid spaceId, Guid botId, string undoToken, CancellationToken cancellationToken = default) =>
        Post<MemoryNoteResponse>($"{Notes(spaceId, botId)}/notes/restore", new RestoreNoteRequest(undoToken), cancellationToken);

    #endregion

    #region Private Methods

    private static string Notes(Guid spaceId, Guid botId) => $"{Space(spaceId)}/bots/{botId}/memory";

    #endregion
}
