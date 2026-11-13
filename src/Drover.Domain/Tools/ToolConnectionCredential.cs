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

    /// <summary>
    /// When the background renewal last tried this credential and did not renew it. Null when it never failed, or
    /// was renewed since. A credential tried lately waits its turn, so one tool server that is down cannot take
    /// every pass from the sign-ins that can be renewed.
    /// </summary>
    public DateTimeOffset? RenewAttemptedAt { get; set; }

    public CredentialStatus Status { get; set; } = CredentialStatus.Active;

    #endregion
}
