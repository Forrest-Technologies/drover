namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Every value the ChatGPT plan's sign-in by code and its runs use, under <c>Drover:Models:SignIn:openai_signin:Code</c>,
/// so an operator can follow a change at the provider without a release. A device that cannot listen for the browser's
/// return signs in this way: the person enters a short code on the provider's page and the server polls until they
/// approve. The flow, its client identifier and its request shapes are those of the provider's own command line tool:
/// the provider documents the flow for that tool (https://learn.chatgpt.com/docs/auth, "Login on headless devices",
/// read 2026-11-27, which also says the person turns device code sign-in on in ChatGPT's security settings), and every
/// value below was read from that tool's published source on 2026-11-27 (its login crate: the device code module, the
/// OAuth client, the auth manager and revoke modules; its API crate: the model list endpoint, the shared header names
/// and the error mapping), except where a value says it is unconfirmed. No value is a secret: the flow has none.
/// </summary>
public sealed class OpenAIPlanCodeOptions
{
    #region Properties

    /// <summary>The tool's public client identifier. Confirmed: the auth manager's <c>CLIENT_ID</c>.</summary>
    public string ClientId { get; set; } = "app_EMoamEEZ73f0CkXaXp7hrann";

    /// <summary>
    /// Asks for a code: POST JSON <c>{ client_id }</c>, answered with <c>device_auth_id</c>, <c>user_code</c> and
    /// <c>interval</c> (seconds, sent as text). Confirmed: <c>{issuer}/api/accounts/deviceauth/usercode</c>.
    /// </summary>
    public Uri StartAddress { get; set; } = new("https://auth.openai.com/api/accounts/deviceauth/usercode");

    /// <summary>Where the person enters the code. Confirmed: <c>{issuer}/codex/device</c>.</summary>
    public Uri PageAddress { get; set; } = new("https://auth.openai.com/codex/device");

    /// <summary>
    /// Asked until the person approves: POST JSON <c>{ device_auth_id, user_code }</c>. A 403 or 404 means not yet; a
    /// success carries <c>authorization_code</c> and <c>code_verifier</c>. Confirmed:
    /// <c>{issuer}/api/accounts/deviceauth/token</c>.
    /// </summary>
    public Uri PollAddress { get; set; } = new("https://auth.openai.com/api/accounts/deviceauth/token");

    /// <summary>
    /// The exchange (form: <c>grant_type=authorization_code</c>, <c>client_id</c>, <c>code</c>, <c>code_verifier</c>,
    /// <c>redirect_uri</c>) and the refresh (JSON: <c>grant_type=refresh_token</c>, <c>client_id</c>,
    /// <c>refresh_token</c>; refresh tokens rotate). Confirmed: <c>{issuer}/oauth/token</c>; the tool sends the
    /// exchange form encoded and the refresh as JSON.
    /// </summary>
    public Uri TokenAddress { get; set; } = new("https://auth.openai.com/oauth/token");

    /// <summary>
    /// Ends the renewable session on sign-out: POST JSON <c>{ token, token_type_hint: "refresh_token", client_id }</c>.
    /// Confirmed: the revoke module's <c>REVOKE_TOKEN_URL</c>.
    /// </summary>
    public Uri RevokeAddress { get; set; } = new("https://auth.openai.com/oauth/revoke");

    /// <summary>The return address the exchange names. Confirmed: <c>{issuer}/deviceauth/callback</c>.</summary>
    public Uri RedirectUri { get; set; } = new("https://auth.openai.com/deviceauth/callback");

    /// <summary>
    /// How long a code lasts. Confirmed: the tool tells the person a code "expires in 15 minutes" and stops polling
    /// then; the start answer names no lifetime.
    /// </summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The base address of runs (<c>POST codex/responses</c>) and the model list (<c>GET codex/models</c>). Confirmed: the
    /// tool's ChatGPT base address and its <c>/codex</c> paths.
    /// </summary>
    public Uri ApiAddress { get; set; } = new("https://chatgpt.com/backend-api/");

    /// <summary>
    /// Headers on every run and model list call, besides the bearer token and <c>chatgpt-account-id</c> (confirmed: the
    /// shared header names). <c>originator</c> names the calling app; the tool sends its own name, Drover sends its.
    /// The <c>OpenAI-Beta</c> value is unconfirmed against the current source.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["originator"] = "drover",
        ["OpenAI-Beta"] = "responses=experimental",
        ["accept"] = "text/event-stream",
    };

    /// <summary>
    /// The <c>client_version</c> the model list is asked with (confirmed: the query parameter). The value is unconfirmed:
    /// the list answers the models a tool of that version can use.
    /// </summary>
    public string ClientVersion { get; set; } = "0.159.3";

    #endregion
}
