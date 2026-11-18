namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What an address of the workspace says: the bot being talked to, what is open beside its main conversation, and
/// what a return from somewhere else left in the query. Every part is optional; the start page has none.
/// </summary>
/// <param name="BotId">The bot being talked to. Without one, the crew's lead is.</param>
/// <param name="Rail">What is open beside the main conversation: "summary", "memory", "screen", "files", "activity", or "side".</param>
/// <param name="SideChat">The side chat that is open: its thread, or "new" for one its first message will make.</param>
/// <param name="ActivityRunId">An earlier run of the main conversation, opened in the activity rail from its reply.</param>
/// <param name="WorkBotId">The bot whose side of handed work is open beside the conversation.</param>
/// <param name="WorkThreadId">That bot's thread for the work.</param>
/// <param name="FormerThreadId">A thread opened by the address it had before conversations were reached through their bot.</param>
/// <param name="Held">The request the person took over, on the screen's address, so a reload keeps them holding it.</param>
/// <param name="ToolSignIn">Set when the app comes back from a tool server's sign-in page: "connected" or "failed".</param>
/// <param name="ToolSignInConnection">The tool connection that sign-in was for.</param>
public sealed record TalkAddress(
    Guid? BotId = null,
    string? Rail = null,
    string? SideChat = null,
    Guid? ActivityRunId = null,
    Guid? WorkBotId = null,
    Guid? WorkThreadId = null,
    Guid? FormerThreadId = null,
    Guid? Held = null,
    string? ToolSignIn = null,
    Guid? ToolSignInConnection = null)
{
    #region Public Methods

    /// <summary>Reads the parts from a matched address, by the names the screen's address patterns give them.</summary>
    public static TalkAddress From<TScreen>(RouteMatch<TScreen> route) => new(
        route.GuidParameter(nameof(BotId)),
        route.Parameter(nameof(Rail)),
        route.Parameter(nameof(SideChat)),
        route.GuidParameter(nameof(ActivityRunId)),
        route.GuidParameter(nameof(WorkBotId)),
        route.GuidParameter(nameof(WorkThreadId)),
        route.GuidParameter(nameof(FormerThreadId)),
        Guid.TryParse(route.QueryValue("held"), out var held) ? held : null,
        route.QueryValue("tool-sign-in"),
        Guid.TryParse(route.QueryValue("connection"), out var connection) ? connection : null);

    #endregion
}
