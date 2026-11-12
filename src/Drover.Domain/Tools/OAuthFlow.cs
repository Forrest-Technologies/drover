namespace Drover.Domain.Tools;

/// <summary>
/// A sign-in in progress, found again by the hash of its <c>state</c> when the provider calls back. The callback
/// has no tenant yet, so <c>sys.resolve_oauth_state</c> maps the hash to a space and a user.
/// </summary>
public sealed class OAuthFlow : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public required string StateHash { get; set; }

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid ToolConnectionId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    #endregion
}
