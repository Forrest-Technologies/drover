namespace Drover.Server.Security;

/// <summary>
/// The address people open this server at, when an operator has said it. Sign-ins to tool servers send a person's
/// browser away and back, so they need it; nothing guesses it from a request.
/// </summary>
public static class PublicAddress
{
    #region Public Methods

    /// <summary>The setting, or else the environment variable. Null when neither holds a whole http or https address.</summary>
    public static Uri? Read(IConfiguration configuration)
    {
        var stated = configuration[Setting] is { Length: > 0 } setting ? setting : configuration[Variable];
        return Uri.TryCreate(stated, UriKind.Absolute, out var address) && address.Scheme is "http" or "https" ? address : null;
    }

    #endregion

    #region Constants

    public const string Setting = "Drover:PublicUrl";

    public const string Variable = "DROVER_PUBLIC_URL";

    #endregion
}
