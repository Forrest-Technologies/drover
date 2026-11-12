namespace Drover.Domain.Secrets;

/// <summary>
/// Encrypted material. The ciphertext is bound to the space and the secret id, so a copy in another row does not
/// decrypt. Never returned by any endpoint; no contract type carries it.
/// </summary>
public sealed class Secret : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public SecretKind Kind { get; set; }

    public required byte[] Ciphertext { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    #endregion

    #region Public Methods

    public override string ToString() => "[secret]";

    #endregion
}
