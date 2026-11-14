namespace Drover.Engine.Accounts;

/// <summary>
/// Signed-in sessions, one row per browser. The session cookie names its row, and every request checks the row is
/// still there, so ending a session on the server ends it for a copied cookie too. Each method opens its own tenant
/// scope with only the user set.
/// </summary>
public sealed class SessionService(SessionRepository sessions, TenantScope scope, TenantContext tenant, TimeProvider time)
{
    #region Properties

    /// <summary>How long a session lasts without being used. The session cookie expires after the same time.</summary>
    public static TimeSpan IdleLimit { get; } = TimeSpan.FromDays(14);

    #endregion

    #region Public Methods

    /// <summary>Starts a session and returns its id. Sessions of the same person left idle past the limit are removed.</summary>
    public async Task<Guid> Open(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        await sessions.RemoveIdleSince(now - IdleLimit, cancellationToken);
        var session = new UserSession { UserId = userId, CreatedAt = now, LastSeenAt = now };
        sessions.Add(session);
        await work.Commit(cancellationToken);
        return session.Id;
    }

    public async Task<bool> IsOpen(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var open = await sessions.IsOpen(sessionId, cancellationToken);
        await work.Commit(cancellationToken);
        return open;
    }

    /// <summary>Records that the session is in use. Returns false when it has been ended.</summary>
    public async Task<bool> Renew(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var open = await sessions.Touch(sessionId, time.GetUtcNow(), cancellationToken);
        await work.Commit(cancellationToken);
        return open;
    }

    public async Task End(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        await sessions.Remove(sessionId, cancellationToken);
        await work.Commit(cancellationToken);
    }

    #endregion
}
