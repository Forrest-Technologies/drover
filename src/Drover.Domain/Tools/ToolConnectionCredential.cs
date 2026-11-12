namespace Drover.Domain.Tools;

/// <summary>
/// A credential for a tool connection. An everyone audience is a credential shared by the whole space; a user's
/// audience is readable only inside a transaction running for that user.
/// </summary>
public sealed class ToolConnectionCredential : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; } = Audience.Everyone;

    public Guid ToolConnectionId { get; set; }

    public Guid SecretId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public CredentialStatus Status { get; set; } = CredentialStatus.Active;

    #endregion
}
