namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Every value the ChatGPT plan sign-in and its runs use, under <c>Drover:Models:SignIn:openai_signin</c>, so an
/// operator can follow a change at the provider without a release. Nothing else in code repeats one of these values.
/// The defaults are the provider's own, read on 2026-11-27 from https://developers.openai.com/siwc/token-sharing-open-source
/// and its pages <c>/sign-in</c>, <c>/profiles-and-sessions</c>, <c>/models-and-inference</c>, <c>/token-reference</c>
/// and <c>/errors-and-recovery</c> (the single-file export https://developers.openai.com/siwc/llms-full.txt), and from the
/// provider's discovery document https://auth.openai.com/.well-known/openid-configuration. No value is a secret: the
/// flow has none.
/// </summary>
public sealed class OpenAIPlanOptions
{
    #region Private Fields

    /// <summary>The configuration section the server binds this from.</summary>
    public const string Section = $"Drover:Models:SignIn:{OpenAIPlanProvider.ConnectionKind}";

    #endregion

    #region Properties

    /// <summary>Where the person's browser goes to approve ("Start authorization").</summary>
    public Uri AuthorizeAddress { get; set; } = new("https://auth.openai.com/api/accounts/authorize");

    /// <summary>The code exchange and the refresh ("Exchange the code", "Refreshing tokens").</summary>
    public Uri TokenAddress { get; set; } = new("https://auth.openai.com/api/accounts/oauth/token");

    /// <summary>
    /// Ends the renewable session on sign-out: the discovery document's <c>revocation_endpoint</c> ("End the renewable
    /// session"). The address #1028 gives, https://auth.openai.com/oauth/revoke, is not the one the document names.
    /// </summary>
    public Uri RevokeAddress { get; set; } = new("https://auth.openai.com/api/accounts/oauth/revoke");

    /// <summary>The provider's published signing keys, which the ID token is checked against (the discovery document's <c>jwks_uri</c>).</summary>
    public Uri KeysAddress { get; set; } = new("https://auth.openai.com/.well-known/jwks.json");

    /// <summary>The ID token's issuer, exactly as the discovery document gives it.</summary>
    public string Issuer { get; set; } = "https://auth.openai.com";

    /// <summary>
    /// Where the person's device receives the browser's return. The provider takes a loopback callback on
    /// <c>127.0.0.1</c> where only the port may vary; the scheme, host and path must stay as they are.
    /// </summary>
    public Uri RedirectUri { get; set; } = new("http://127.0.0.1:1455/auth/callback");

    /// <summary>
    /// The client id a first sign-in registers with. The provider then issues a client id bound to the person, which
    /// every later sign-in and refresh of that person uses instead.
    /// </summary>
    public string RegistrationClientId { get; set; } = "dynamic_agent_client";

    /// <summary>The app's name, sent as <c>agent_name_hint</c> on a first sign-in only. The person can change it before approving.</summary>
    public string AgentName { get; set; } = "Drover";

    /// <summary>The identity scopes and the plan scopes, in one space-separated list.</summary>
    public string Scopes { get; set; } = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";

    /// <summary>The scope that allows plan use. A sign-in whose token answer does not grant it is not kept.</summary>
    public string RequiredScope { get; set; } = "chatgpt.tokens.use.direct";

    /// <summary>The <c>resource</c> sent on the approval, the exchange and every refresh.</summary>
    public string Resource { get; set; } = "https://api.openai.com/v1";

    /// <summary>The address runs and the model list go to. Only <c>GET models</c> and <c>POST responses</c> are called on it.</summary>
    public Uri ApiAddress { get; set; } = new("https://api.openai.com/v1/");

    /// <summary>The sign-in by code, for a device that cannot listen for the browser's return, and its runs.</summary>
    public OpenAIPlanCodeOptions Code { get; set; } = new();

    #endregion
}
