namespace Drover.Engine.Accounts;

/// <summary>
/// Changing a password, and resetting a forgotten one with a code the operator reads from the server log. A change
/// ends every other session of the person; a reset ends all of them. The reset code is stored only as a hash, works
/// once, and runs out after <see cref="ResetCodeLifetime"/>. Each method opens its own tenant scopes, with only the
/// user set once the user is known.
/// </summary>
public sealed class PasswordService(
    UserManager<User> users,
    IPasswordHasher<User> hasher,
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
        var user = await ReadAccount(userId, cancellationToken);

        if (user?.PasswordHash is null)
        {
            return PasswordResult.Refused;
        }

        // A numbered attempt from the session, taken in one statement, as sign-in takes one from its bucket. 0 means
        // the session is gone.
        var attempt = await InOwnTransaction(
            () => sessions.TakePasswordChangeAttempt(sessionId, cancellationToken),
            cancellationToken);

        if (attempt == 0)
        {
            return new PasswordResult(PasswordOutcome.SessionEnded, []);
        }

        // Hashed outside any transaction. Only an attempt within the limit is checked against the real password.
        var limit = identity.Value.Lockout.MaxFailedAccessAttempts;
        var mayCheck = attempt <= limit;
        var verdict = hasher.VerifyHashedPassword(
            user,
            mayCheck ? user.PasswordHash : DecoyPassword.Hash,
            currentPassword);

        if (!mayCheck || verdict is PasswordVerificationResult.Failed)
        {
            return attempt >= limit ? await EndSession(userId, sessionId, cancellationToken) : PasswordResult.Refused;
        }

        var problems = await ProblemsWith(user, newPassword);

        if (problems.Count > 0)
        {
            await InOwnTransaction(
                () => sessions.ForgetFailedPasswordChanges(sessionId, cancellationToken),
                cancellationToken);
            return new PasswordResult(PasswordOutcome.Invalid, problems);
        }

        // The new password is hashed before the one short write, which touches only the password and the stamp.
        var newHash = hasher.HashPassword(user, newPassword);
        var stamp = NewSecurityStamp();
        await using var work = await scope.Begin(cancellationToken);

        if (!await accounts.ReplacePassword(userId, user.PasswordHash, newHash, stamp, cancellationToken))
        {
            return PasswordResult.Refused;
        }

        await sessions.ForgetFailedPasswordChanges(sessionId, cancellationToken);
        await sessions.RemoveAllBut(sessionId, cancellationToken);
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
    /// Sets a new password with a reset code, uses the code up, lifts any lockout in either bucket, and ends every
    /// session of the person. A wrong, used, or expired code and an unknown email are refused alike.
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
        var problems = await ProblemsWith(new User(), newPassword);

        if (problems.Count > 0)
        {
            return new PasswordResult(PasswordOutcome.Invalid, problems);
        }

        // Hashed first, outside any transaction, whatever the code turns out to be; then one short transaction.
        var newHash = hasher.HashPassword(new User { Id = userId }, newPassword);
        var stamp = NewSecurityStamp();
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var stored = await accounts.TakeToken(userId, TokenProvider, ResetTokenName, cancellationToken);
        var codeIsRight = ResetCodeMatches(stored, code);

        // A refusal leaves the transaction uncommitted, so a wrong code does not use up the right one.
        if (found is null || !codeIsRight)
        {
            return PasswordResult.Refused;
        }

        await accounts.ResetPassword(userId, newHash, stamp, cancellationToken);
        await sessions.RemoveAll(cancellationToken);
        await work.Commit(cancellationToken);
        revocations.Announce(userId);
        return new PasswordResult(PasswordOutcome.Changed, []);
    }

    #endregion

    #region Password Change

    /// <summary>Ends the session that sent a wrong current password as often as sign-in allows.</summary>
    private async Task<PasswordResult> EndSession(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await InOwnTransaction(() => sessions.Remove(sessionId, cancellationToken), cancellationToken);
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

    /// <summary>A fresh security stamp, which ends every cookie issued with the old one at its next check.</summary>
    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20));

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
