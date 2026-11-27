namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// What the OpenRouter sign-in reads, under <c>Drover:Models:SignIn:openrouter_signin</c>. Every literal the flow
/// needs is here and nowhere else in code, so following a change of the provider needs no release. The flow has no
/// client id and no client secret: the provider registers nobody.
/// Source (read 2026-11-27): https://openrouter.ai/docs/use-cases/oauth-pkce. The page gives the authorization address
/// <c>https://openrouter.ai/auth</c> with <c>callback_url</c>, <c>code_challenge</c>, <c>code_challenge_method=S256</c>
/// and an optional <c>state</c> that comes back unchanged, the exchange <c>POST https://openrouter.ai/api/v1/auth/keys</c>
/// with a JSON body of <c>code</c>, <c>code_verifier</c> and <c>code_challenge_method</c> answering <c>key</c>, and says
/// localhost callbacks work on any port and an <c>https</c> address is the way for a hosted app. The API address
/// <c>https://openrouter.ai/api/v1</c> is the one the key kinds use (https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion).
/// Unconfirmed: whether <c>127.0.0.1</c> is accepted as a callback host (the page names <c>localhost</c> only, so
/// the sign-in always sends <c>localhost</c>), and whether <c>state</c> survives an <c>https</c> callback that carries a path.
/// </summary>
public sealed class OpenRouterSignInOptions
{
    #region Private Fields

    public const string Section = "Drover:Models:SignIn:openrouter_signin";

    #endregion

    #region Properties

    public Uri AuthorizeAddress { get; set; } = new("https://openrouter.ai/auth");

    public Uri TokenAddress { get; set; } = new("https://openrouter.ai/api/v1/auth/keys");

    /// <summary>Where the desktop head receives the browser's return. Only the port may vary.</summary>
    public Uri RedirectUri { get; set; } = new("http://localhost:1455/auth/callback");

    public Uri ApiAddress { get; set; } = new("https://openrouter.ai/api/v1/");

    /// <summary>The label the provider prefills for the key it creates, so the person can tell it from their others.</summary>
    public string KeyLabel { get; set; } = "Drover";

    #endregion
}
