namespace Drover.Domain.Permissions;

/// <summary>
/// What one bot may do with one tool. No row means the bot cannot see the tool. The effective grant is never read
/// from this row alone: it is the minimum along the bot's path to the root (<see cref="GrantRules"/>).
/// </summary>
public sealed class ToolGrant : ISpaceOwned
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid BotId { get; set; }

    public Guid ToolId { get; set; }

    public GrantEffect Effect { get; set; }

    public ReviewState ReviewState { get; set; } = ReviewState.Current;

    public Guid? GrantedByUserId { get; set; }

    public Guid? GrantedByBotId { get; set; }

    #endregion
}
