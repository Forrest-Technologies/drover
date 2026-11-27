namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// What the Copilot plan sign-in and its runs read, under <c>Drover:Models:SignIn:copilot_signin</c>. Every address
/// and name the flow needs is here, so following a change at GitHub needs no release. There is no compiled-in client
/// id: the operator registers a GitHub OAuth app or GitHub App and sets <see cref="ClientId"/> (and
/// <see cref="ClientSecret"/> for a browser return).
/// </summary>
public sealed class CopilotPlanOptions
{
    #region Private Fields

    public const string Section = "Drover:Models:SignIn:copilot_signin";

    public const string UserAgent = "Drover";

    #endregion

    #region Properties

    /// <summary>The client id of the operator's GitHub app. Empty until the operator sets it; sign-in then refuses.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret of a confidential GitHub app, used on the token exchange and refresh. Empty until the
    /// operator sets it; a browser return then refuses.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    public Uri AuthorizeAddress { get; set; } = new("https://github.com/login/oauth/authorize");

    public Uri DeviceCodeAddress { get; set; } = new("https://github.com/login/device/code");

    public Uri TokenAddress { get; set; } = new("https://github.com/login/oauth/access_token");

    public Uri UserAddress { get; set; } = new("https://api.github.com/user");

    public Uri RevokeAddress { get; set; } = new("https://api.github.com/applications");

    public Uri ApiAddress { get; set; } = new("https://api.individual.githubcopilot.com/");

    /// <summary>Where a person reviews Copilot on their GitHub account.</summary>
    public Uri UsagePage { get; set; } = new("https://github.com/settings/copilot");

    public string Scope { get; set; } = "read:user";

    /// <summary>Where the desktop head receives the browser's return. Only the port may vary.</summary>
    public Uri RedirectUri { get; set; } = new("http://127.0.0.1:1455/auth/callback");

    #endregion
}
