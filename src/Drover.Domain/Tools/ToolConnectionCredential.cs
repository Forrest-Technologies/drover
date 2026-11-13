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

    /// <summary>
    /// When this credential's tokens were last replaced by new ones. Null for a credential that is not a sign-in,
    /// and for a sign-in whose first tokens are still the ones in use. The background renewal leaves one renewed lately alone, so a tool server
    /// that hands out very short tokens is not asked for new ones at every pass.
    /// </summary>
    public DateTimeOffset? RenewedAt { get; set; }

    public CredentialStatus Status { get; set; } = CredentialStatus.Active;

    #endregion
}
