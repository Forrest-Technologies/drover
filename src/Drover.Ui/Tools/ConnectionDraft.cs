namespace Drover.Ui.Tools;

/// <summary>
/// A new tool connection as its form holds it. Only what the chosen kind of credential uses is sent, so a value
/// typed under one choice and then abandoned never leaves the page.
/// </summary>
public sealed class ConnectionDraft
{
    #region Properties

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>"none", "header", or "oauth".</summary>
    public string AuthKind { get; set; } = "none";

    public string HeaderName { get; set; } = "Authorization";

    public string HeaderValue { get; set; } = string.Empty;

    /// <summary>"per_user" or "space".</summary>
    public string SignInScope { get; set; } = "per_user";

    public bool OwnClient { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public bool Trusted { get; set; }

    public string ProtocolMode { get; set; } = "automatic";

    #endregion

    #region Public Methods

    public CreateToolConnectionRequest ToRequest()
    {
        var header = AuthKind is "header";
        var signIn = AuthKind is "oauth";
        var ownClient = signIn && OwnClient;
        return new(
            Name.Trim(),
            Url.Trim(),
            AuthKind,
            header ? HeaderName.Trim() : null,
            header ? HeaderValue : null,
            Trusted ? "trusted" : "untrusted",
            ProtocolMode,
            ownClient ? ClientId.Trim() : null,
            ownClient && ClientSecret.Length > 0 ? ClientSecret : null,
            signIn ? SignInScope : null,
            ownClient ? Issuer.Trim() : null);
    }

    #endregion
}
