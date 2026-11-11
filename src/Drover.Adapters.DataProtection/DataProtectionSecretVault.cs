namespace Drover.Adapters.DataProtection;

/// <summary>
/// <see cref="ISecretVault"/> over ASP.NET Core Data Protection, with the ciphertext in the <c>secrets</c> table.
/// The protector's purpose is built from the space id and the secret id, so a ciphertext copied into another
/// space's row or another secret's row does not decrypt. Runs inside the caller's tenant scope.
/// </summary>
public sealed class DataProtectionSecretVault(
    IDataProtectionProvider protection,
    SecretRepository secrets,
    TenantContext tenant) : ISecretVault
{
    #region Private Fields

    private const string PurposeRoot = "Drover.Secrets.v1";

    #endregion

    #region Interface Implementations

    public Task<Guid> Store(SecretKind kind, string value, Guid? privateToUserId, CancellationToken cancellationToken)
    {
        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("No space is set for this scope.");
        var userId = tenant.UserId ?? throw new InvalidOperationException("No user is set for this scope.");
        var id = Guid.CreateVersion7();
        secrets.Add(new Secret
        {
            Id = id,
            SpaceId = spaceId,
            AudienceUserId = privateToUserId ?? Audience.Everyone,
            Kind = kind,
            Ciphertext = Protect(spaceId, id, value),
            CreatedByUserId = userId,
        });
        return Task.FromResult(id);
    }

    public async Task Replace(Guid secretId, string value, CancellationToken cancellationToken)
    {
        var secret = await FindSecret(secretId, cancellationToken);
        secret.Ciphertext = Protect(secret.SpaceId, secret.Id, value);
    }

    public async Task<string> Reveal(Guid secretId, CancellationToken cancellationToken)
    {
        var secret = await FindSecret(secretId, cancellationToken);
        return Encoding.UTF8.GetString(ProtectorFor(secret.SpaceId, secret.Id).Unprotect(secret.Ciphertext));
    }

    public async Task Delete(Guid secretId, CancellationToken cancellationToken)
    {
        var secret = await FindSecret(secretId, cancellationToken);
        secrets.Remove(secret);
    }

    #endregion

    #region Private Methods

    private async Task<Secret> FindSecret(Guid secretId, CancellationToken cancellationToken) =>
        await secrets.GetSecret(secretId, cancellationToken)
        ?? throw new KeyNotFoundException($"Secret {secretId} was not found.");

    private byte[] Protect(Guid spaceId, Guid secretId, string value) =>
        ProtectorFor(spaceId, secretId).Protect(Encoding.UTF8.GetBytes(value));

    private IDataProtector ProtectorFor(Guid spaceId, Guid secretId) =>
        protection.CreateProtector($"{PurposeRoot} / {spaceId} / {secretId}");

    #endregion
}
