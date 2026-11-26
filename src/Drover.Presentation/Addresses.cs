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

    /// <summary>Where a person chooses which bot gets what another app shared.</summary>
    public const string Share = "/share";

    public const string SetupModel = "/setup/model";

    public const string SetupBot = "/setup/bot";

    public const string Bots = "/bots";

    public const string Projects = "/projects";

    public const string Models = "/models";

    public const string Tools = "/tools";

    public const string Extensions = "/extensions";

    public const string Status = "/status";

    public const string Usage = "/usage";

    #endregion

    #region Public Methods

    /// <summary>Any conversation, by its id. The page sends it on to its bot, as the main conversation or a side chat.</summary>
    public static string Conversation(Guid threadId) => $"/threads/{threadId}";

    /// <summary>A bot's main conversation.</summary>
    public static string Talk(Guid botId) => $"/talk/{botId}";

    /// <summary>The summary the server keeps of a bot's main conversation.</summary>
    public static string Summary(Guid botId) => $"/talk/{botId}/summary";

    /// <summary>What a bot remembers about the person, beside its main conversation.</summary>
    public static string Memory(Guid botId) => $"/talk/{botId}/memory";

    /// <summary>A bot's sandbox screen, beside its main conversation.</summary>
    public static string Screen(Guid botId) => $"/talk/{botId}/screen";

    /// <summary>The newest run of a bot's main conversation, as a timeline beside it.</summary>
    public static string Activity(Guid botId) => $"/talk/{botId}/activity";

    /// <summary>An earlier run of a bot's main conversation, as a timeline beside it.</summary>
    public static string Activity(Guid botId, Guid runId) => $"/talk/{botId}/activity/{runId}";

    /// <summary>The files a person and a bot exchanged, beside its main conversation.</summary>
    public static string Files(Guid botId) => $"/talk/{botId}/files";

    /// <summary>The moment a message was said, in the main conversation or in a side chat.</summary>
    public static string Moment(Guid botId, Guid threadId, Guid messageId, bool inMainConversation) =>
        $"{(inMainConversation ? Talk(botId) : SideChat(botId, threadId))}#{MessageAnchors.Anchor(messageId)}";

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

    /// <summary>The editor on a bot that is not made yet, beneath <paramref name="parentBotId"/> when there is one.</summary>
    public static string NewBot(Guid? parentBotId = null) => parentBotId is { } parent ? $"/bots/new?parent={parent}" : "/bots/new";

    /// <summary>One project: its instructions, its files, and its conversations.</summary>
    public static string Project(Guid projectId) => $"/projects/{projectId}";

    /// <summary>A bot's editor with its Memory section open.</summary>
    public static string BotMemory(Guid botId) => $"/bots/{botId}?section=memory";

    /// <summary>A bot's editor with its Limits section open.</summary>
    public static string BotLimits(Guid botId) => $"/bots/{botId}?section=limits";

    /// <summary>A bot's sandbox: its screen, its terminal, and its power controls.</summary>
    public static string BotSandbox(Guid botId) => $"/bots/{botId}/sandbox";

    /// <summary>A bot's sandbox with the terminal open.</summary>
    public static string BotTerminal(Guid botId) => $"{BotSandbox(botId)}?tab=terminal";

    /// <summary>What the bots used in a period, with one bot's most expensive runs listed when <paramref name="botId"/> is given.</summary>
    public static string UsageFor(string period, Guid? botId = null) =>
        botId is { } id ? $"{Usage}?period={period}&bot={id}" : $"{Usage}?period={period}";

    /// <summary>The tool connections screen, saying how a sign-in at a provider ended.</summary>
    public static string ToolSignIn(string outcome, string? connectionId) =>
        connectionId is null ? $"/tools?sign-in={outcome}" : $"/tools?sign-in={outcome}&connection={connectionId}";

    /// <summary>The tool connections screen, ending a sign-in whose browser came back into a native app.</summary>
    public static string ToolSignInReturn(string ticket) => $"/tools?ticket={Uri.EscapeDataString(ticket)}";

    #endregion
}
