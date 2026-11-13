namespace Drover.Ui;

/// <summary>
/// The addresses of the screens, in one place so a link and its page cannot drift apart.
/// </summary>
public static class Addresses
{
    #region Properties

    public const string Home = "/";

    public const string NewThread = "/new";

    public const string SignIn = "/sign-in";

    public const string Setup = "/setup";

    public const string SetupModel = "/setup/model";

    public const string SetupBot = "/setup/bot";

    public const string Bots = "/bots";

    public const string Models = "/models";

    public const string Tools = "/tools";

    public const string Status = "/status";

    #endregion

    #region Public Methods

    public static string Thread(Guid threadId) => $"/threads/{threadId}";

    public static string Bot(Guid botId) => $"/bots/{botId}";

    /// <summary>The tool connections screen, saying how a sign-in at a provider ended.</summary>
    public static string ToolSignIn(string outcome, string? connectionId) =>
        connectionId is null ? $"/tools?sign-in={outcome}" : $"/tools?sign-in={outcome}&connection={connectionId}";

    #endregion
}
