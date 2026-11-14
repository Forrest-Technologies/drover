namespace Drover.Data.Repositories;

/// <summary>One space a person can pick, with their role in it.</summary>
public sealed record SpaceMembership(Guid SpaceId, string Kind, string Name, string Slug, string Role);

/// <summary>
/// Accounts, spaces, and memberships. Every method runs inside a tenant scope the service has opened and commits.
/// The cross-space lookups go through the functions in schema <c>sys</c> and return ids or the caller's own rows.
/// </summary>
public sealed class AccountRepository(DroverDbContext db)
{
    #region Private Fields

    private const long FirstRunLockKey = 0x44524F56_45520002;

    #endregion

    #region Public Methods

    /// <summary>Sign-in knows an email and no space. Returns the user's id, or null when nobody has that email.</summary>
    public Task<Guid?> FindUserIdForSignIn(string email, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<Guid?>($"SELECT user_id AS \"Value\" FROM sys.find_user_for_sign_in({email})")
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<long> CountUsers(CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<long>($"SELECT user_count AS \"Value\" FROM sys.health()")
            .SingleAsync(cancellationToken);

    /// <summary>Makes two first-run requests take turns, in this server and in any other on the same database.</summary>
    public Task LockFirstRun(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({FirstRunLockKey})", cancellationToken);

    /// <summary>
    /// Takes one sign-in attempt from a lockout bucket in a single statement: adds one to the bucket's count and
    /// returns the new count, or null when the bucket is locked or the account is not there. Each attempt gets its
    /// own number, so attempts at the same moment cannot all read the same count. Where lockout is off for the
    /// account, the attempt is taken and 0 returned. No lock outlives the statement.
    /// </summary>
    public async Task<int?> TakeSignInAttempt(
        Guid userId,
        bool knownBrowser,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var counts = knownBrowser
            ? await db.Database.SqlQuery<int>(
                $"""
                UPDATE users
                SET known_browser_access_failed_count =
                    known_browser_access_failed_count + CASE WHEN lockout_enabled THEN 1 ELSE 0 END
                WHERE id = {userId}
                  AND (NOT lockout_enabled OR known_browser_lockout_end IS NULL OR known_browser_lockout_end <= {now})
                RETURNING CASE WHEN lockout_enabled THEN known_browser_access_failed_count ELSE 0 END AS "Value"
                """).ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<int>(
                $"""
                UPDATE users
                SET access_failed_count = access_failed_count + CASE WHEN lockout_enabled THEN 1 ELSE 0 END
                WHERE id = {userId} AND (NOT lockout_enabled OR lockout_end IS NULL OR lockout_end <= {now})
                RETURNING CASE WHEN lockout_enabled THEN access_failed_count ELSE 0 END AS "Value"
                """).ToListAsync(cancellationToken);
        return counts.Count == 0 ? null : counts[0];
    }

    /// <summary>
    /// After a wrong password: when the attempt's number reached the limit, locks the bucket until
    /// <paramref name="lockUntil"/> and starts its count again. Otherwise changes nothing. One statement.
    /// </summary>
    public Task RecordWrongPassword(
        Guid userId,
        bool knownBrowser,
        int attempt,
        int limit,
        DateTimeOffset lockUntil,
        CancellationToken cancellationToken) =>
        knownBrowser
            ? db.Database.ExecuteSqlAsync(
                $"""
                UPDATE users SET known_browser_lockout_end = {lockUntil}, known_browser_access_failed_count = 0
                WHERE id = {userId} AND lockout_enabled AND {attempt} >= {limit}
                """,
                cancellationToken)
            : db.Database.ExecuteSqlAsync(
                $"""
                UPDATE users SET lockout_end = {lockUntil}, access_failed_count = 0
                WHERE id = {userId} AND lockout_enabled AND {attempt} >= {limit}
                """,
                cancellationToken);

    /// <summary>
    /// After the right password: starts the bucket's count again, only if the bucket is still unlocked, and says
    /// whether it was. An attempt that hashed while another locked the bucket is refused. One statement.
    /// </summary>
    public async Task<bool> RecordRightPassword(
        Guid userId,
        bool knownBrowser,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accepted = knownBrowser
            ? await db.Database.SqlQuery<int>(
                $"""
                UPDATE users SET known_browser_access_failed_count = 0
                WHERE id = {userId}
                  AND (NOT lockout_enabled OR known_browser_lockout_end IS NULL OR known_browser_lockout_end <= {now})
                RETURNING 1 AS "Value"
                """).ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<int>(
                $"""
                UPDATE users SET access_failed_count = 0
                WHERE id = {userId} AND (NOT lockout_enabled OR lockout_end IS NULL OR lockout_end <= {now})
                RETURNING 1 AS "Value"
                """).ToListAsync(cancellationToken);
        return accepted.Count > 0;
    }

    /// <summary>
    /// Removes one of the user's stored tokens and returns its value, in one statement, or null when there is none.
    /// Two transactions taking the same token cannot both get it: the second waits for the first and then finds
    /// nothing, or finds the token again if the first rolled back. Runs with only the user set.
    /// </summary>
    public async Task<string?> TakeToken(Guid userId, string provider, string name, CancellationToken cancellationToken)
    {
        var values = await db.Database
            .SqlQuery<string>(
                $"""
                DELETE FROM user_tokens
                WHERE user_id = {userId} AND login_provider = {provider} AND name = {name}
                RETURNING value AS "Value"
                """)
            .ToListAsync(cancellationToken);
        return values.SingleOrDefault();
    }

    /// <summary>
    /// Sets a new password hash and security stamp, only while the stored hash is still
    /// <paramref name="expectedHash"/>, and says whether it was. Touches no other column, so it cannot overwrite the sign-in counters another attempt
    /// is changing at the same moment. One statement.
    /// </summary>
    public async Task<bool> ReplacePassword(
        Guid userId,
        string expectedHash,
        string newHash,
        string newStamp,
        CancellationToken cancellationToken)
    {
        var replaced = await db.Database
            .SqlQuery<int>(
                $"""
                UPDATE users
                SET password_hash = {newHash}, security_stamp = {newStamp},
                    concurrency_stamp = {Guid.NewGuid().ToString()}
                WHERE id = {userId} AND password_hash = {expectedHash}
                RETURNING 1 AS "Value"
                """)
            .ToListAsync(cancellationToken);
        return replaced.Count > 0;
    }

    /// <summary>Sets a new password hash and stamp and lifts the lockout in both buckets. One statement.</summary>
    public Task ResetPassword(Guid userId, string newHash, string newStamp, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            UPDATE users
            SET password_hash = {newHash}, security_stamp = {newStamp}, concurrency_stamp = {Guid.NewGuid().ToString()},
                access_failed_count = 0, lockout_end = NULL,
                known_browser_access_failed_count = 0, known_browser_lockout_end = NULL
            WHERE id = {userId}
            """,
            cancellationToken);

    /// <summary>
    /// Stores the same password hashed in the current format, only while the old hash is still there. The security
    /// stamp stays, since the password did not change. One statement.
    /// </summary>
    public Task UpgradePasswordHash(Guid userId, string oldHash, string newHash, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"UPDATE users SET password_hash = {newHash} WHERE id = {userId} AND password_hash = {oldHash}",
            cancellationToken);

    /// <summary>The signed-in user's own spaces. The user comes from the transaction.</summary>
    public Task<List<SpaceMembership>> ListMemberships(CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<SpaceMembership>(
                $"""
                SELECT space_id, kind, name, slug, role
                FROM sys.memberships_for_user()
                """)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The user's role in a space, or null when they are not a member. Runs with only the user set: the
    /// <c>memberships</c> policy lets a user read their own rows, and nothing else is touched.
    /// </summary>
    public async Task<MembershipRole?> FindRole(Guid spaceId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await db.Memberships
            .AsNoTracking()
            .Where(membership => membership.SpaceId == spaceId && membership.UserId == userId)
            .Select(membership => (MembershipRole?)membership.Role)
            .SingleOrDefaultAsync(cancellationToken);
        return role;
    }

    public Task<Space?> GetSpace(Guid id, CancellationToken cancellationToken) =>
        db.Spaces.AsNoTracking().SingleOrDefaultAsync(space => space.Id == id, cancellationToken);

    public void Add(Space space) => db.Spaces.Add(space);

    public void Add(Membership membership) => db.Memberships.Add(membership);

    #endregion
}
