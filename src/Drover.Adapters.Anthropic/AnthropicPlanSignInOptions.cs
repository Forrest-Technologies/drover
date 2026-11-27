namespace Drover.Adapters.Anthropic;

/// <summary>
/// What the Claude plan sign-in and its runs read, under <c>Drover:Models:SignIn:anthropic_signin</c>. Every literal
/// value the flow needs is here and nowhere else in code, so following a change of the provider needs no release. The
/// flow has no client secret.
/// </summary>
/// <remarks>
/// The provider documents no sign-in for other apps: these are the client identifier, addresses, scopes, headers and
/// system prefix of the provider's own command line tool, as the working open source reference client of #1028 sends
/// them (read 2026-11-27 at its commit 129c326a: its Anthropic sign-in module for the flow, its Messages module for the
/// headers and the prefix). Every value below matches that source except the <c>user-agent</c> version, where the
/// source has moved on from the issue's <c>2.1.280</c> to <c>2.1.322</c> and the newer one is used. Unconfirmed,
/// because no working source shows it: whether the provider accepts a <c>state</c> that is not the PKCE verifier (the
/// source sends the verifier; Drover sends its own random state, so the verifier never travels in an address), and
/// whether the model list answers a plan token. The owner's <c>Live</c> test checks both.
/// </remarks>
public sealed class AnthropicPlanSignInOptions
{
    #region Private Fields

    public const string Section = "Drover:Models:SignIn:anthropic_signin";

    #endregion

    #region Properties

    public string ClientId { get; set; } = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>The provider's page where the person approves. It takes <c>code=true</c> and the usual PKCE parameters.</summary>
    public Uri StartAddress { get; set; } = new("https://claude.ai/oauth/authorize");

    public string Scopes { get; set; } = "org:create_api_key user:profile user:inference user:sessions:claude_code user:mcp_servers user:file_upload";

    /// <summary>Where the desktop head receives the browser's return. Only the port may vary: the provider accepts any loopback port.</summary>
    public Uri RedirectUri { get; set; } = new("http://localhost:53692/callback");

    /// <summary>The provider's page that shows <c>code#state</c> to copy, for a device that cannot listen.</summary>
    public Uri CodePageRedirectUri { get; set; } = new("https://platform.claude.com/oauth/code/callback");

    /// <summary>Exchange and refresh, both JSON. Refresh tokens rotate.</summary>
    public Uri TokenAddress { get; set; } = new("https://platform.claude.com/v1/oauth/token");

    /// <summary>Added to every model call and model list on a plan connection, by name.</summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic-beta"] = "claude-code-20250219,oauth-2025-04-20",
        ["user-agent"] = "claude-cli/2.1.322",
        ["x-app"] = "cli",
    };

    /// <summary>The first system block of every call, before the bot's instructions.</summary>
    public string SystemPrefix { get; set; } = "You are Claude Code, Anthropic's official CLI for Claude.";

    #endregion
}
