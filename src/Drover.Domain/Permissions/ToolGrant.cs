namespace Drover.Domain.Permissions;

/// <summary>
/// What one bot may do with one tool. No row means the bot cannot see the tool. The effective grant is never read
/// from this row alone: inherited access is bounded by the path to the nearest direct grant or to the root.
/// </summary>
public sealed class ToolGrant : ISpaceOwned
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid BotId { get; set; }

    public Guid ToolId { get; set; }

    public GrantEffect Effect { get; set; }

    public ReviewState ReviewState { get; set; } = ReviewState.Current;

    /// <summary>An explicit connected-tool grant made by a space administrator for this bot.</summary>
    public bool IsDirect { get; set; }

    public Guid? GrantedByUserId { get; set; }

    public Guid? GrantedByBotId { get; set; }

    #endregion
}
