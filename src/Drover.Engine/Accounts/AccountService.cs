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
    TenantContext tenant)
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

        var user = NewUser(userId, email, displayName);
        var created = await users.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            return new SetupResult(SetupOutcome.Invalid, Guid.Empty, [.. created.Errors.Select(error => error.Description)]);
        }

        accounts.Add(PersonalSpace(spaceId, displayName));
        accounts.Add(new Membership { SpaceId = spaceId, UserId = userId, Role = MembershipRole.Owner });
        await work.Commit(cancellationToken);
        return new SetupResult(SetupOutcome.Created, userId, [], await users.GetSecurityStampAsync(user));
    }

    /// <summary>Checks an email and password, counting failures and locking the account as Identity is set to.</summary>
    public async Task<CredentialResult> CheckCredentials(string email, string password, CancellationToken cancellationToken)
    {
        var userId = await FindUserId(email, cancellationToken);

        if (userId is not { } id)
        {
            hasher.VerifyHashedPassword(new User(), decoyHash.Value, password);
            return CredentialResult.Refused;
        }

        tenant.SetUser(id);
        await using var work = await scope.Begin(cancellationToken);
        var result = await VerifyPassword(id, password);
        await work.Commit(cancellationToken);
        return result;
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

    private async Task<Guid?> FindUserId(string email, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var userId = await accounts.FindUserIdForSignIn(email, cancellationToken);
        await work.Commit(cancellationToken);
        return userId;
    }

    private async Task<CredentialResult> VerifyPassword(Guid userId, string password)
    {
        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return CredentialResult.Refused;
        }

        // The password is hashed before the lockout decision, so a locked account takes as long to refuse as any other.
        var passwordIsRight = await users.CheckPasswordAsync(user, password);

        if (await users.IsLockedOutAsync(user))
        {
            return CredentialResult.Refused;
        }

        if (!passwordIsRight)
        {
            await users.AccessFailedAsync(user);
            return CredentialResult.Refused;
        }

        await users.ResetAccessFailedCountAsync(user);
        return new CredentialResult(
            true,
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            await users.GetSecurityStampAsync(user) ?? string.Empty);
    }

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
