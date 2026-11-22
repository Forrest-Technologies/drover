namespace Drover.Data.Repositories;

/// <summary>A sandbox secret with the bots it is for. The value is never loaded with it.</summary>
public sealed record SandboxSecretWithBots(SandboxSecret Secret, IReadOnlyList<Guid> BotIds);

/// <summary>Sandbox secrets and the bots each is for. The caller owns the tenant scope and commits it.</summary>
public sealed class SandboxSecretRepository(DroverDbContext db)
{
    #region Private Fields

    // Mixed into the advisory lock key so it cannot collide with another lock taken on the same space.
    private const long SecretLockSeed = 0x44524F56_4552000A;

    #endregion

    #region Public Methods

    public void Add(SandboxSecret secret) => db.SandboxSecrets.Add(secret);

    public void Remove(SandboxSecret secret) => db.SandboxSecrets.Remove(secret);

    /// <summary>
    /// Holds the space's sandbox secrets still until the transaction ends, so two additions count one after the
    /// other and two changes of one name cannot both pass the check for a clash.
    /// </summary>
    public Task LockSpaceSecrets(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(sys.current_space()::text, {SecretLockSeed}))",
            cancellationToken);

    public Task<int> CountSecrets(CancellationToken cancellationToken) => db.SandboxSecrets.CountAsync(cancellationToken);

    /// <summary>The tracked row, or null when it does not exist or row-level security hides it.</summary>
    public Task<SandboxSecret?> GetSecret(Guid id, CancellationToken cancellationToken) =>
        db.SandboxSecrets.SingleOrDefaultAsync(secret => secret.Id == id, cancellationToken);

    public async Task<List<SandboxSecretWithBots>> ListSecrets(CancellationToken cancellationToken)
    {
        var secrets = await db.SandboxSecrets.AsNoTracking()
            .OrderBy(secret => secret.Name)
            .ThenBy(secret => secret.Id)
            .ToListAsync(cancellationToken);
        var links = await db.SandboxSecretBots.AsNoTracking().ToListAsync(cancellationToken);
        var bySecret = links.ToLookup(link => link.SandboxSecretId, link => link.BotId);
        return [.. secrets.Select(secret => new SandboxSecretWithBots(secret, [.. bySecret[secret.Id]]))];
    }

    public Task<List<Guid>> ListBotIds(Guid secretId, CancellationToken cancellationToken) =>
        db.SandboxSecretBots.Where(link => link.SandboxSecretId == secretId)
            .Select(link => link.BotId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Replaces the bots a secret is for. Each link carries the secret's name, so the unique index refuses a bot that
    /// another secret already gives this name.
    /// </summary>
    public async Task SetBots(SandboxSecret secret, IReadOnlyCollection<Guid> botIds, CancellationToken cancellationToken)
    {
        await db.SandboxSecretBots.Where(link => link.SandboxSecretId == secret.Id).ExecuteDeleteAsync(cancellationToken);
        db.SandboxSecretBots.AddRange(botIds.Select(botId => new SandboxSecretBot
        {
            SandboxSecretId = secret.Id,
            BotId = botId,
            Name = secret.Name,
        }));
    }

    /// <summary>Whether a secret other than <paramref name="exceptId"/> already gives one of these bots this name.</summary>
    public Task<bool> AnyBotHasName(string name, IReadOnlyCollection<Guid> botIds, Guid exceptId, CancellationToken cancellationToken) =>
        db.SandboxSecretBots.AnyAsync(
            link => link.Name == name && botIds.Contains(link.BotId) && link.SandboxSecretId != exceptId,
            cancellationToken);

    /// <summary>Whether another secret already holds this name for the whole space, or in this project.</summary>
    public Task<bool> AnyWithName(string name, SandboxSecretScope scope, Guid? projectId, Guid exceptId, CancellationToken cancellationToken) =>
        db.SandboxSecrets.AnyAsync(
            secret => secret.Name == name && secret.Scope == scope && secret.ProjectId == projectId && secret.Id != exceptId,
            cancellationToken);

    /// <summary>
    /// Every secret a bot's sandbox may be given: those for the whole space, those for this bot, and those of every
    /// project, since the broker decides per request which project's value applies.
    /// </summary>
    public Task<List<SandboxSecret>> ListSecretsReachingBot(Guid botId, CancellationToken cancellationToken) =>
        db.SandboxSecrets.AsNoTracking()
            .Where(secret => secret.Scope != SandboxSecretScope.Bots
                || db.SandboxSecretBots.Any(link => link.SandboxSecretId == secret.Id && link.BotId == botId))
            .OrderBy(secret => secret.Name)
            .ThenBy(secret => secret.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The display names of these people, for those the caller may read: fellow members of the space. Anyone else is
    /// left out.
    /// </summary>
    public Task<Dictionary<Guid, string>> ListDisplayNames(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        db.UserProfiles.AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, profile => profile.DisplayName, cancellationToken);

    /// <summary>The bots a secret reaches: every bot for the space or a project, else the chosen ones.</summary>
    public Task<List<Guid>> ListOwnersReached(SandboxSecret secret, CancellationToken cancellationToken) =>
        secret.Scope is SandboxSecretScope.Bots
            ? ListBotIds(secret.Id, cancellationToken)
            : db.Bots.Where(bot => bot.Status != BotStatus.Removed).Select(bot => bot.Id).ToListAsync(cancellationToken);

    /// <summary>The project's secrets, tracked, for removing them with the project.</summary>
    public Task<List<SandboxSecret>> GetProjectSecrets(Guid projectId, CancellationToken cancellationToken) =>
        db.SandboxSecrets.Where(secret => secret.ProjectId == projectId).ToListAsync(cancellationToken);

    #endregion
}
