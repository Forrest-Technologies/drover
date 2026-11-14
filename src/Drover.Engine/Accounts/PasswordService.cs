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
    SessionRevocations revocations,
    TenantScope scope,
    TenantContext tenant,
    IOptions<IdentityOptions> identity,
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
    /// <paramref name="sessionId"/>. A wrong current password is counted against this session, not the sign-in
    /// lockout: strangers who locked sign-in cannot block a signed-in change, and a session that sends a wrong current
    /// password as often as sign-in allows is ended, so guessing through a session stays bounded.
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
        await accounts.LockUser(userId, cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return PasswordResult.Refused;
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);

        if (changed.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            return await CountFailedChange(userId, sessionId, work, cancellationToken);
        }

        if (!changed.Succeeded)
        {
            return Invalid(changed);
        }

        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        await sessions.ForgetFailedPasswordChanges(sessionId, cancellationToken);
        await sessions.RemoveAllBut(sessionId, cancellationToken);
        var stamp = await users.GetSecurityStampAsync(user);
        await work.Commit(cancellationToken);
        revocations.Announce(userId);
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
    /// Sets a new password with a reset code, uses the code up, lifts any lockout in either bucket, and ends every session of the
    /// person. A wrong, used, or expired code and an unknown email are refused alike.
    /// </summary>
    public async Task<PasswordResult> RedeemResetCode(
        string email,
        string code,
        string newPassword,
        CancellationToken cancellationToken)
    {
        // An unknown email takes the same steps against an account id nobody has, so the time a refusal takes does
        // not say which emails exist. Row-level security shows that id nothing.
        var found = await FindUserId(email, cancellationToken);
        var userId = found ?? Guid.CreateVersion7();
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var stored = await accounts.TakeToken(userId, TokenProvider, ResetTokenName, cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        var codeIsRight = ResetCodeMatches(stored, code);

        // A refusal leaves the transaction uncommitted, so a wrong code does not use up the right one.
        if (found is null || user is null || !codeIsRight)
        {
            return PasswordResult.Refused;
        }

        var problems = await ProblemsWith(user, newPassword);

        if (problems.Count > 0)
        {
            return new PasswordResult(PasswordOutcome.Invalid, problems);
        }

        EnsureSucceeded(await users.RemovePasswordAsync(user));
        EnsureSucceeded(await users.AddPasswordAsync(user, newPassword));
        user.KnownBrowserAccessFailedCount = 0;
        user.KnownBrowserLockoutEnd = null;
        EnsureSucceeded(await users.SetLockoutEndDateAsync(user, null));
        EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
        await sessions.RemoveAll(cancellationToken);
        await work.Commit(cancellationToken);
        revocations.Announce(userId);
        return new PasswordResult(PasswordOutcome.Changed, []);
    }

    #endregion

    #region Password Change

    private async Task<PasswordResult> CountFailedChange(
        Guid userId,
        Guid sessionId,
        TenantWork work,
        CancellationToken cancellationToken)
    {
        var failures = await sessions.CountFailedPasswordChange(sessionId, cancellationToken);

        if (failures < identity.Value.Lockout.MaxFailedAccessAttempts)
        {
            await work.Commit(cancellationToken);
            return PasswordResult.Refused;
        }

        await sessions.Remove(sessionId, cancellationToken);
        await work.Commit(cancellationToken);
        revocations.Announce(userId);
        return new PasswordResult(PasswordOutcome.SessionEnded, []);
    }

    #endregion

    #region Reset Codes

    /// <summary>Hashes the guess whether or not a code was stored, so both refusals take the same work.</summary>
    private bool ResetCodeMatches(string? stored, string code)
    {
        var guess = Encoding.ASCII.GetBytes(Hash(Normalize(code)));

        if (stored?.Split(':') is not [var expires, var hash] || !long.TryParse(expires, out var expiresAt))
        {
            CryptographicOperations.FixedTimeEquals(guess, guess);
            return false;
        }

        var unexpired = time.GetUtcNow() < DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), guess) && unexpired;
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
