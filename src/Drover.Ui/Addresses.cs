namespace Drover.Ui;

/// <summary>
/// The addresses of the screens, in one place so a link and its page cannot drift apart.
/// </summary>
public static class Addresses
{
    #region Properties

    public const string Home = "/";

    public const string SignIn = "/sign-in";

    public const string Setup = "/setup";

    public const string SetupModel = "/setup/model";

    public const string SetupBot = "/setup/bot";

    public const string Bots = "/bots";

    public const string Tools = "/tools";

    public const string Status = "/status";

    #endregion

    #region Public Methods

    /// <summary>A bot's main conversation.</summary>
    public static string Talk(Guid botId) => $"/talk/{botId}";

    /// <summary>The summary the server keeps of a bot's main conversation.</summary>
    public static string Summary(Guid botId) => $"/talk/{botId}/summary";

    /// <summary>A bot's sandbox screen, beside its main conversation.</summary>
    public static string Screen(Guid botId) => $"/talk/{botId}/screen";

    /// <summary>The side chats a person has with a bot.</summary>
    public static string SideChats(Guid botId) => $"/talk/{botId}/side";

    /// <summary>A side chat that its first message will make.</summary>
    public static string NewSideChat(Guid botId) => $"/talk/{botId}/side/new";

    public static string SideChat(Guid botId, Guid threadId) => $"/talk/{botId}/side/{threadId}";

    /// <summary>
    /// A bot's side of work handed to it, opened to read beside the conversation with <paramref name="botId"/>, where
    /// the work was shown.
    /// </summary>
    public static string Work(Guid botId, Guid workerId, Guid threadId) => $"/talk/{botId}/work/{workerId}/{threadId}";

    public static string Bot(Guid botId) => $"/bots/{botId}";

    /// <summary>A bot's sandbox: its screen, its terminal, and its power controls.</summary>
    public static string BotSandbox(Guid botId) => $"/bots/{botId}/sandbox";

    /// <summary>The tool connections screen, saying how a sign-in at a provider ended.</summary>
    public static string ToolSignIn(string outcome, string? connectionId) =>
        connectionId is null ? $"/tools?sign-in={outcome}" : $"/tools?sign-in={outcome}&connection={connectionId}";

    #endregion
}
