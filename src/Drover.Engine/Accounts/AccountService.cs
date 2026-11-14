namespace Drover.Engine.Accounts;

/// <summary>
/// Creating accounts and checking credentials. Both run before any space is chosen, so this service opens its
/// own tenant scopes: one with nothing set for the sign-in lookup, one with only the user set for the account's
/// own row, and one with the new user and the new space for creating the account.
/// </summary>
public sealed class AccountService(
    UserManager<User> users,
    IPasswordHasher<User> hasher,
    AccountRepository accounts,
    TenantScope scope,
    TenantContext tenant,
    ToolCatalog catalog,
    IOptions<IdentityOptions> identity,
    TimeProvider time)
{
    #region Private Fields

    // Checked against when the email is unknown, so that a refusal takes about as long either way.
    private static readonly Lazy<string> decoyHash =
        new(() => new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString("N")));

    #endregion

    #region Public Methods

    public async Task<bool> IsSetupOpen(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var count = await accounts.CountUsers(cancellationToken);
        await work.Commit(cancellationToken);
        return count == 0;
    }

    /// <summary>
    /// Creates the owner, their personal space, and the owner membership in one transaction, and only while there
    /// are no users. Two requests take turns on a database lock, so setup works exactly once.
    /// </summary>
    public async Task<SetupResult> CreateFirstAccount(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var userId = Guid.CreateVersion7();
        var spaceId = Guid.CreateVersion7();
        tenant.Set(spaceId, userId);
        await using var work = await scope.Begin(cancellationToken);
        await accounts.LockFirstRun(cancellationToken);

        if (await accounts.CountUsers(cancellationToken) > 0)
        {
            return new SetupResult(SetupOutcome.Closed, Guid.Empty, []);
        }

        var result = await AddAccount(NewUser(userId, email, displayName), password, spaceId, cancellationToken);

        if (result.Status is SetupOutcome.Created)
        {
            await work.Commit(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Creates an account for someone who registered themselves, with a personal space of their own, and records
    /// the version of the terms they accepted. The caller has already checked the deployment allows registration.
    /// </summary>
    public async Task<SetupResult> Register(
        string email,
        string displayName,
        string password,
        string? acceptedTermsVersion,
        CancellationToken cancellationToken)
    {
        if (await FindUserId(email, cancellationToken) is not null)
        {
            return new SetupResult(SetupOutcome.Invalid, Guid.Empty, ["That email cannot be used to register."]);
        }

        var userId = Guid.CreateVersion7();
        var spaceId = Guid.CreateVersion7();
        tenant.Set(spaceId, userId);
        await using var work = await scope.Begin(cancellationToken);
        var user = NewUser(userId, email, displayName);
        user.TermsVersion = acceptedTermsVersion;
        user.TermsAcceptedAt = acceptedTermsVersion is null ? null : DateTimeOffset.UtcNow;
        var result = await AddAccount(user, password, spaceId, cancellationToken);

        if (result.Status is SetupOutcome.Created)
        {
            await work.Commit(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Checks an email and password, counting failures and locking the account as Identity is set to. Failures are
    /// counted in one of two buckets: from a browser that has signed in to this account before
    /// (<paramref name="marker"/> names this account at its current security stamp), or from anyone else. Each
    /// bucket locks on its own, so strangers cannot keep the owner's own browser out, and guessing stays bounded.
    /// </summary>
    public async Task<CredentialResult> CheckCredentials(
        string email,
        string password,
        BrowserMarker? marker,
        CancellationToken cancellationToken)
    {
        // An unknown email takes every step below against an account id nobody has, which row-level security shows
        // nothing, so how long a refusal takes, alone or with others in parallel, does not say which emails exist.
        var userId = await FindUserId(email, cancellationToken) ?? Guid.CreateVersion7();
        tenant.SetUser(userId);
        var user = await ReadAccount(userId, cancellationToken);
        var knownBrowser = user is not null && marker?.Names(user) is true;
        var lockout = identity.Value.Lockout;
        var now = time.GetUtcNow();
        var attempt = await InOwnTransaction(
            () => accounts.TakeSignInAttempt(userId, knownBrowser, now, cancellationToken),
            cancellationToken);

        // Hashed once, outside any transaction, so no lock is held while it runs. A guess is checked against the
        // real password only with an attempt the bucket allowed; otherwise against the decoy, which costs the same.
        var mayCheck = user?.PasswordHash is not null && attempt <= lockout.MaxFailedAccessAttempts;
        var verdict = hasher.VerifyHashedPassword(
            user ?? new User(),
            mayCheck ? user!.PasswordHash! : decoyHash.Value,
            password);

        if (!mayCheck || verdict is PasswordVerificationResult.Failed)
        {
            await InOwnTransaction(
                () => accounts.RecordWrongPassword(
                    userId,
                    knownBrowser,
                    attempt ?? 0,
                    lockout.MaxFailedAccessAttempts,
                    time.GetUtcNow() + lockout.DefaultLockoutTimeSpan,
                    cancellationToken),
                cancellationToken);
            return CredentialResult.Refused;
        }

        var accepted = await InOwnTransaction(
            () => accounts.RecordRightPassword(userId, knownBrowser, time.GetUtcNow(), cancellationToken),
            cancellationToken);
        return accepted ? Accepted(user!) : CredentialResult.Refused;
    }

    /// <summary>The user's current security stamp, or null when the user no longer exists. Runs with only the user set.</summary>
    public async Task<string?> GetSecurityStamp(Guid userId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        var stamp = user is null ? null : await users.GetSecurityStampAsync(user);
        await work.Commit(cancellationToken);
        return stamp;
    }

    /// <summary>The spaces the signed-in person can pick from. The user comes from the tenant context.</summary>
    public async Task<IReadOnlyList<SpaceMembership>> ListSpaces(Guid userId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var memberships = await accounts.ListMemberships(cancellationToken);
        await work.Commit(cancellationToken);
        return memberships;
    }

    /// <summary>
    /// The person's role in the space, or null when they are not a member. Only the user is set while it runs, so
    /// no space data is read before the answer.
    /// </summary>
    public async Task<MembershipRole?> FindRole(Guid spaceId, Guid userId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var role = await accounts.FindRole(spaceId, userId, cancellationToken);
        await work.Commit(cancellationToken);
        return role;
    }

    #endregion

    #region Private Methods

    private async Task<SetupResult> AddAccount(User user, string password, Guid spaceId, CancellationToken cancellationToken)
    {
        var created = await users.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            return new SetupResult(SetupOutcome.Invalid, Guid.Empty, [.. created.Errors.Select(error => error.Description)]);
        }

        accounts.Add(PersonalSpace(spaceId, user.DisplayName));
        accounts.Add(new Membership { SpaceId = spaceId, UserId = user.Id, Role = MembershipRole.Owner });

        // A space starts with the built-in tools as rows, so its first bot's editor already lists them.
        await catalog.EnsureBuiltins(cancellationToken);
        return new SetupResult(SetupOutcome.Created, user.Id, [], await users.GetSecurityStampAsync(user));
    }

    private async Task<Guid?> FindUserId(string email, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var userId = await accounts.FindUserIdForSignIn(email, cancellationToken);
        await work.Commit(cancellationToken);
        return userId;
    }

    private async Task<User?> ReadAccount(Guid userId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        await work.Commit(cancellationToken);
        return user;
    }

    /// <summary>Runs one statement in a transaction of its own, so any row lock it takes ends with it.</summary>
    private async Task<T> InOwnTransaction<T>(Func<Task<T>> statement, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var result = await statement();
        await work.Commit(cancellationToken);
        return result;
    }

    private async Task InOwnTransaction(Func<Task> statement, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        await statement();
        await work.Commit(cancellationToken);
    }

    private static CredentialResult Accepted(User user) =>
        new(true, user.Id, user.Email ?? string.Empty, user.DisplayName, user.SecurityStamp ?? string.Empty);

    private static User NewUser(Guid userId, string email, string displayName) => new()
    {
        Id = userId,
        UserName = email,
        Email = email,
        DisplayName = displayName,
    };

    private static Space PersonalSpace(Guid spaceId, string displayName) => new()
    {
        Id = spaceId,
        Kind = SpaceKind.Personal,
        Name = displayName,
        Slug = $"personal-{spaceId:N}",
    };

    #endregion
}
