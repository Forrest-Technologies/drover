namespace Drover.Domain.Models;

/// <summary>
/// One person's own sign-in to a model connection. The audience is always that one person, never everyone, so the
/// database shows the row only inside a transaction running for them: a run acting for someone else cannot find it.
/// The token set is in the vault (<see cref="SecretId"/>), private to the same person.
/// </summary>
public sealed class ModelConnectionCredential : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid ModelConnectionId { get; set; }

    /// <summary>
    /// The token set in the vault, private to the same person. A plain column, not a foreign key: this row is always
    /// one person's and may never be shared, so it cannot take part in the audience-carrying key a shared parent
    /// would widen. Both rows are visible to that person alone.
    /// </summary>
    public Guid SecretId { get; set; }

    /// <summary>What the provider says the account is called. Shown to the person it belongs to only.</summary>
    public string? AccountLabel { get; set; }

    /// <summary>When the access token runs out, or null when the provider gave no end.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When the tokens were last replaced by a refresh, or first stored. The renewal leaves one renewed lately alone.</summary>
    public DateTimeOffset? RenewedAt { get; set; }

    /// <summary>
    /// When the background renewal last took this sign-in up and did not renew it, whether it was tried or passed
    /// over. Null when that never happened, or it was renewed since. A sign-in taken up lately waits its turn, so
    /// sign-ins that cannot be renewed do not take every pass from those that can.
    /// </summary>
    public DateTimeOffset? RenewAttemptedAt { get; set; }

    public ModelSignInStatus Status { get; set; } = ModelSignInStatus.Active;

    #endregion
}
