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
    /// Locks the user's own row until the transaction ends, so two sign-in attempts on one account take turns: each
    /// reads the failure count the other committed. Runs with only the user set; the row is visible to its user.
    /// </summary>
    public Task LockUser(Guid userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {userId} FOR UPDATE", cancellationToken);

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
