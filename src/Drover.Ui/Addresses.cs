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

    #endregion

    #region Public Methods

    public static string Thread(Guid threadId) => $"/threads/{threadId}";

    #endregion
}
