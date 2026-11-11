namespace Drover.Domain.Secrets;

/// <summary>
/// Protects and reveals secrets by id. Every method runs inside a tenant scope the caller has already opened, and
/// the caller commits it. <see cref="Reveal"/> is called by adapters at the moment of use and nowhere else: no
/// endpoint returns a secret value. A ciphertext copied into another space's row or another secret's row must not
/// decrypt: <see cref="Reveal"/> throws <see cref="System.Security.Cryptography.CryptographicException"/>. A secret
/// that does not exist, or that the caller cannot see, throws <see cref="KeyNotFoundException"/>.
/// </summary>
public interface ISecretVault
{
    Task<Guid> Store(SecretKind kind, string value, Guid? privateToUserId, CancellationToken cancellationToken);

    Task Replace(Guid secretId, string value, CancellationToken cancellationToken);

    Task<string> Reveal(Guid secretId, CancellationToken cancellationToken);

    Task Delete(Guid secretId, CancellationToken cancellationToken);
}
