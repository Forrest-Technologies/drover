namespace Drover.Data.Identity;

/// <summary>
/// One signed-in browser. The session cookie names this row, and a cookie whose row is gone is refused, so signing
/// out, changing the password, or a reset ends a session on the server and not only in the browser.
/// </summary>
public sealed class UserSession : IUserOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the session was last checked against the database. Idle sessions are removed after the cookie's lifetime.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Wrong current passwords sent from this session to change the password. Counted per session, so strangers'
    /// sign-in failures do not block a signed-in change, and a session that guesses too often is ended.
    /// </summary>
    public int FailedPasswordChanges { get; set; }

    #endregion
}
