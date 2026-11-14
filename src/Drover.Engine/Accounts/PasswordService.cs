namespace Drover.Engine.Accounts;

/// <summary>
/// Changing a password, and resetting a forgotten one with a code the operator reads from the server log. A change
/// ends every other session of the person; a reset ends all of them. The reset code is stored only as a hash, works
/// once, and runs out after <see cref="ResetCodeLifetime"/>. Each method opens its own tenant scopes, with only the
/// user set once the user is known.
/// </summary>
public sealed class PasswordService(
    UserManager<User> users,
    AccountRepository accounts,
    SessionRepository sessions,
    TenantScope scope,
    TenantContext tenant,
    TimeProvider time)
{
    #region Private Fields

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const string TokenProvider = "Drover";

    private const string ResetTokenName = "PasswordReset";

    #endregion

    #region Properties

    public static TimeSpan ResetCodeLifetime { get; } = TimeSpan.FromMinutes(15);

    #endregion

    #region Public Methods

    /// <summary>
    /// Changes the password of the signed-in person, given the current one, and ends every session of theirs but
    /// <paramref name="sessionId"/>. A wrong current password counts toward the lockout, as at sign-in.
    /// </summary>
    public async Task<PasswordResult> Change(
        Guid userId,
        Guid sessionId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null || await users.IsLockedOutAsync(user))
        {
            return PasswordResult.Refused;
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);

        if (changed.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            await users.AccessFailedAsync(user);
            await work.Commit(cancellationToken);
            return PasswordResult.Refused;
        }

        if (!changed.Succeeded)
        {
            return Invalid(changed);
        }

        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        await sessions.RemoveAllBut(sessionId, cancellationToken);
        var stamp = await users.GetSecurityStampAsync(user);
        await work.Commit(cancellationToken);
        return new PasswordResult(PasswordOutcome.Changed, [], stamp);
    }

    /// <summary>
    /// Makes a one-time reset code for the account with this email and returns it for the operator to read, or null
    /// when no account has the email. Only a hash of the code is stored, and a new code replaces any earlier one.
    /// </summary>
    public async Task<string?> IssueResetCode(string email, CancellationToken cancellationToken)
    {
        if (await FindUserId(email, cancellationToken) is not { } userId)
        {
            return null;
        }

        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return null;
        }

        var code = RandomNumberGenerator.GetString(Alphabet, 12);
        var expiresAt = time.GetUtcNow() + ResetCodeLifetime;
        EnsureSucceeded(await users.SetAuthenticationTokenAsync(
            user,
            TokenProvider,
            ResetTokenName,
            $"{expiresAt.ToUnixTimeSeconds()}:{Hash(code)}"));
        await work.Commit(cancellationToken);
        return $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    /// <summary>
    /// Sets a new password with a reset code, uses the code up, lifts any lockout, and ends every session of the
    /// person. A wrong, used, or expired code and an unknown email are refused alike.
    /// </summary>
    public async Task<PasswordResult> RedeemResetCode(
        string email,
        string code,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (await FindUserId(email, cancellationToken) is not { } userId)
        {
            return PasswordResult.Refused;
        }

        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null || !await HoldsResetCode(user, code))
        {
            return PasswordResult.Refused;
        }

        var problems = await ProblemsWith(user, newPassword);

        if (problems.Count > 0)
        {
            return new PasswordResult(PasswordOutcome.Invalid, problems);
        }

        EnsureSucceeded(await users.RemoveAuthenticationTokenAsync(user, TokenProvider, ResetTokenName));
        EnsureSucceeded(await users.RemovePasswordAsync(user));
        EnsureSucceeded(await users.AddPasswordAsync(user, newPassword));
        EnsureSucceeded(await users.SetLockoutEndDateAsync(user, null));
        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        await sessions.RemoveAll(cancellationToken);
        await work.Commit(cancellationToken);
        return new PasswordResult(PasswordOutcome.Changed, []);
    }

    #endregion

    #region Reset Codes

    private async Task<bool> HoldsResetCode(User user, string code)
    {
        var stored = await users.GetAuthenticationTokenAsync(user, TokenProvider, ResetTokenName);

        if (stored?.Split(':') is not [var expires, var hash] || !long.TryParse(expires, out var expiresAt))
        {
            return false;
        }

        var unexpired = time.GetUtcNow() < DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hash),
            Encoding.ASCII.GetBytes(Hash(Normalize(code))));
        return unexpired && matches;
    }

    private static string Normalize(string code) =>
        code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    #endregion

    #region Private Methods

    private async Task<Guid?> FindUserId(string email, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var userId = await accounts.FindUserIdForSignIn(email, cancellationToken);
        await work.Commit(cancellationToken);
        return userId;
    }

    private async Task<List<string>> ProblemsWith(User user, string password)
    {
        List<string> problems = [];

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            problems.AddRange(result.Errors.Select(error => error.Description));
        }

        return problems;
    }

    private static PasswordResult Invalid(IdentityResult result) =>
        new(PasswordOutcome.Invalid, [.. result.Errors.Select(error => error.Description)]);

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"The account could not be updated: {string.Join(" ", result.Errors.Select(error => error.Code))}");
        }
    }

    #endregion
}
