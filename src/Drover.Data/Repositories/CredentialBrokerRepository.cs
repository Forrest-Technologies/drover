namespace Drover.Data.Repositories;

/// <summary>A sandbox the credential broker was given a token for, as <c>sys.broker_sandbox_for_token</c> returns it: ids only.</summary>
public sealed record BrokerSandbox(Guid SpaceId, Guid SandboxId);


/// <summary>
/// What the credential broker reads and writes: the secrets each sandbox was started with, the space's broker
/// authority, and the sandbox a token belongs to. The caller owns the tenant scope and commits it.
/// </summary>
public sealed class CredentialBrokerRepository(DroverDbContext db)
{
    #region Private Fields

    // Mixed into the advisory lock key so it cannot collide with another lock taken on the same space.
    private const long AuthorityLockSeed = 0x44524F56_4552000B;

    #endregion

    #region Public Methods

    /// <summary>
    /// The live sandbox whose broker token has this hash, in any space. The broker's request carries no space, so this
    /// crosses spaces through <c>sys.broker_sandbox_for_token</c>, which returns ids only; the caller then reads the
    /// sandbox inside that space's own tenant scope. Called with no tenant set.
    /// </summary>
    public async Task<BrokerSandbox?> FindSandboxForToken(byte[] tokenHash, CancellationToken cancellationToken) =>
        (await db.Database
            .SqlQuery<BrokerSandbox>(
                $"""
                SELECT space_id, sandbox_id
                FROM sys.broker_sandbox_for_token({tokenHash})
                """)
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    public void AddEntries(IEnumerable<SandboxSecretEntry> entries) => db.SandboxSecretEntries.AddRange(entries);

    /// <summary>Forgets what a sandbox was started with, before it is started again under the same row.</summary>
    public Task RemoveEntries(Guid sandboxId, CancellationToken cancellationToken) =>
        db.SandboxSecretEntries.Where(entry => entry.SandboxId == sandboxId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>The space's broker authorities still valid at <paramref name="at"/>, the oldest first.</summary>
    public Task<List<BrokerAuthority>> ListAuthorities(DateTimeOffset at, CancellationToken cancellationToken) =>
        db.BrokerAuthorities.AsNoTracking()
            .Where(authority => authority.NotAfter > at)
            .OrderBy(authority => authority.NotAfter)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The secrets a placeholder this sandbox sent stands for, with their rows; none when the placeholder is not this
    /// sandbox's, or its secrets were removed.
    /// </summary>
    public Task<List<SandboxSecret>> ListSecretsForPlaceholder(Guid sandboxId, byte[] placeholderHash, CancellationToken cancellationToken) =>
        db.SandboxSecrets.AsNoTracking()
            .Where(secret => db.SandboxSecretEntries.Any(entry => entry.SandboxId == sandboxId
                && entry.SandboxSecretId == secret.Id
                && entry.PlaceholderHash == placeholderHash))
            .ToListAsync(cancellationToken);

    /// <summary>The live sandboxes started with this secret's real value in them.</summary>
    public Task<List<Sandbox>> ListLiveSandboxesHoldingRaw(Guid sandboxSecretId, CancellationToken cancellationToken) =>
        db.Sandboxes
            .Where(sandbox => (sandbox.State == SandboxState.Creating || sandbox.State == SandboxState.Running || sandbox.State == SandboxState.Paused)
                && db.SandboxSecretEntries.Any(entry => entry.SandboxId == sandbox.Id
                    && entry.SandboxSecretId == sandboxSecretId
                    && entry.PlaceholderHash == null))
            .ToListAsync(cancellationToken);

    /// <summary>The space's broker authority valid past <paramref name="validAfter"/>, the newest first, or null.</summary>
    public Task<BrokerAuthority?> FindAuthority(DateTimeOffset validAfter, CancellationToken cancellationToken) =>
        db.BrokerAuthorities.AsNoTracking()
            .Where(authority => authority.NotAfter > validAfter)
            .OrderByDescending(authority => authority.NotAfter)
            .FirstOrDefaultAsync(cancellationToken);

    public void AddAuthority(BrokerAuthority authority) => db.BrokerAuthorities.Add(authority);

    /// <summary>Makes two first uses of a space's broker take turns, so a space gets one authority.</summary>
    public Task LockAuthority(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(sys.current_space()::text, {AuthorityLockSeed}))",
            cancellationToken);

    /// <summary>Records that a secret was used now, once a minute at most.</summary>
    public Task MarkUsed(Guid sandboxSecretId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.SandboxSecrets
            .Where(secret => secret.Id == sandboxSecretId && (secret.LastUsedAt == null || secret.LastUsedAt < now.AddMinutes(-1)))
            .ExecuteUpdateAsync(update => update.SetProperty(secret => secret.LastUsedAt, now), cancellationToken);

    #endregion
}
