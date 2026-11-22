namespace Drover.Domain.Sandboxes;

/// <summary>
/// The certificate authority a space's sandboxes trust for the credential broker. The private key is in the vault
/// (<see cref="SecretId"/>); the certificate is public and is put into each sandbox's trust bundle. Sandboxes of other
/// spaces never trust it.
/// </summary>
public sealed class BrokerAuthority : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    /// <summary>The certificate, PEM encoded.</summary>
    public required string Certificate { get; set; }

    /// <summary>The private key in the vault. A plain column: the secret has an audience and this row has none.</summary>
    public Guid SecretId { get; set; }

    public DateTimeOffset NotAfter { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
