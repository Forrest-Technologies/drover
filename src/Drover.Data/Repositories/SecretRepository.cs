namespace Drover.Data.Repositories;

/// <summary>What the secrets list may show. The ciphertext is never loaded for it.</summary>
public sealed record SecretMetadata(
    Guid Id,
    SecretKind Kind,
    Guid AudienceUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>Stores and retrieves secret rows. The caller owns the tenant scope and commits it.</summary>
public sealed class SecretRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(Secret secret) => db.Secrets.Add(secret);

    public void Remove(Secret secret) => db.Secrets.Remove(secret);

    /// <summary>The tracked row, or null when it does not exist or row-level security hides it.</summary>
    public Task<Secret?> GetSecret(Guid id, CancellationToken cancellationToken) =>
        db.Secrets.SingleOrDefaultAsync(secret => secret.Id == id, cancellationToken);

    public Task<List<SecretMetadata>> ListSecretMetadata(CancellationToken cancellationToken) =>
        db.Secrets
            .AsNoTracking()
            .OrderBy(secret => secret.CreatedAt)
            .ThenBy(secret => secret.Id)
            .Select(secret => new SecretMetadata(
                secret.Id,
                secret.Kind,
                secret.AudienceUserId,
                secret.CreatedAt,
                secret.LastUsedAt))
            .ToListAsync(cancellationToken);

    #endregion
}
