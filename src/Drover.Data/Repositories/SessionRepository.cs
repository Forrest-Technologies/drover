namespace Drover.Data.Repositories;

/// <summary>
/// The signed-in sessions of one person. Every method runs inside a tenant scope the service has opened, with the
/// user set: the <c>user_sessions</c> policy shows a person only their own rows.
/// </summary>
public sealed class SessionRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(UserSession session) => db.UserSessions.Add(session);

    public Task<bool> IsOpen(Guid sessionId, CancellationToken cancellationToken) =>
        db.UserSessions.AnyAsync(session => session.Id == sessionId, cancellationToken);

    /// <summary>Records that the session was seen. Returns false when it no longer exists.</summary>
    public async Task<bool> Touch(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.UserSessions
            .Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.LastSeenAt, now), cancellationToken) > 0;

    /// <summary>Counts one more wrong current password from the session; the new count, or 0 when it is gone.</summary>
    public async Task<int> CountFailedPasswordChange(Guid sessionId, CancellationToken cancellationToken)
    {
        var counts = await db.Database
            .SqlQuery<int>(
                $"""
                UPDATE user_sessions SET failed_password_changes = failed_password_changes + 1
                WHERE id = {sessionId}
                RETURNING failed_password_changes AS "Value"
                """)
            .ToListAsync(cancellationToken);
        return counts.SingleOrDefault();
    }

    public Task ForgetFailedPasswordChanges(Guid sessionId, CancellationToken cancellationToken) =>
        db.UserSessions
            .Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.FailedPasswordChanges, 0),
                cancellationToken);

    public Task Remove(Guid sessionId, CancellationToken cancellationToken) =>
        db.UserSessions.Where(session => session.Id == sessionId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>Removes every session of the person in the transaction.</summary>
    public Task RemoveAll(CancellationToken cancellationToken) => db.UserSessions.ExecuteDeleteAsync(cancellationToken);

    /// <summary>Removes every session of the person in the transaction except <paramref name="keep"/>.</summary>
    public Task RemoveAllBut(Guid keep, CancellationToken cancellationToken) =>
        db.UserSessions.Where(session => session.Id != keep).ExecuteDeleteAsync(cancellationToken);

    public Task RemoveIdleSince(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.UserSessions.Where(session => session.LastSeenAt < cutoff).ExecuteDeleteAsync(cancellationToken);

    #endregion
}
