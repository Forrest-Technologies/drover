namespace Drover.Data.Repositories;

/// <summary>
/// The notes a bot keeps about a person. Every read names the bot and the person, so a caller never sees a note of
/// another bot, and never one kept for everyone in the space; row-level security keeps every other person's notes
/// out of reach whatever the caller asks. Runs inside the caller's tenant scope.
/// </summary>
public sealed class MemoryRepository(DroverDbContext db)
{
    #region Private Fields

    // Mixed into the advisory lock key so it cannot collide with another lock taken on the same ids.
    private const long NotesLockSeed = 0x44524F56_45520005;

    #endregion

    #region Public Methods

    public void Add(MemoryNote note) => db.Memories.Add(note);

    public void Remove(MemoryNote note) => db.Memories.Remove(note);

    /// <summary>
    /// Makes the rest of the transaction the only one that changes this bot's notes about this person, so two saves
    /// that each fit cannot together pass the limit on their size. Held until the transaction ends.
    /// </summary>
    public Task LockNotes(Guid botId, Guid audienceUserId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({botId}::text || {audienceUserId}::text, {NotesLockSeed}))",
            cancellationToken);

    /// <summary>The bot's notes about the person, oldest first. Not tracked.</summary>
    public Task<List<MemoryNote>> ListNotes(Guid botId, Guid audienceUserId, CancellationToken cancellationToken) =>
        db.Memories.AsNoTracking()
            .Where(note => note.BotId == botId && note.AudienceUserId == audienceUserId)
            .OrderBy(note => note.CreatedAt)
            .ThenBy(note => note.Id)
            .ToListAsync(cancellationToken);

    /// <summary>The tracked note, or null when it is not this bot's note about this person.</summary>
    public Task<MemoryNote?> GetNote(Guid id, Guid botId, Guid audienceUserId, CancellationToken cancellationToken) =>
        db.Memories.SingleOrDefaultAsync(
            note => note.Id == id && note.BotId == botId && note.AudienceUserId == audienceUserId,
            cancellationToken);

    #endregion
}
