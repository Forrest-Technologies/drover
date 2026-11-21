namespace Drover.Domain.Permissions;

/// <summary>
/// A time-bound approval of every ask a bot makes for one person. A window never grants a tool: it only answers
/// asks that the grant and the rules left standing. It is space-owned and addressed to one person, so a run for
/// anybody else is never covered by it.
/// </summary>
public sealed class AllowAllWindow : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    /// <summary>The one bot whose asks the window answers. Windows never flow to bots beneath it.</summary>
    public Guid BotId { get; set; }

    /// <summary>The person the window acts for. Only runs with this principal are covered.</summary>
    public Guid AudienceUserId { get; set; }

    public AllowAllWindowKind Kind { get; set; }

    /// <summary>When the person started the window, by the server's clock.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Set only for the two timed kinds, by the server's clock.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public AllowAllWindowEndReason? EndedReason { get; set; }

    /// <summary>The quick control on a bot, or an approval card for one call.</summary>
    public AllowAllWindowSource StartedFrom { get; set; } = AllowAllWindowSource.Bot;

    /// <summary>
    /// The call whose approval card started the window. Null for the bot's quick control. A plain column: both
    /// tables carry an audience, but with different parents, so a key would need a second audience in it.
    /// </summary>
    public Guid? ToolCallId { get; set; }

    #endregion
}
