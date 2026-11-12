namespace Drover.Engine.Spaces;

/// <summary>
/// Reads for a space the request is already inside. Runs in the tenant scope that the endpoint filter opened for
/// the request, so it never opens one of its own.
/// </summary>
public sealed class SpaceService(AccountRepository accounts, SecretRepository secrets)
{
    #region Public Methods

    public Task<Space?> GetSpace(Guid id, CancellationToken cancellationToken) =>
        accounts.GetSpace(id, cancellationToken);

    public Task<List<SecretMetadata>> ListSecrets(CancellationToken cancellationToken) =>
        secrets.ListSecretMetadata(cancellationToken);

    #endregion
}
