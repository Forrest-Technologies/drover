namespace Drover.Adapters.E2B;

/// <summary>The operator's E2B account. The key is configuration held in memory, never a vault secret or a response.</summary>
public sealed class E2BOptions
{
    #region Properties

    /// <summary>The team API key. E2B keys start with <c>e2b_</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The control plane is <c>api.{Domain}</c> and each sandbox's daemon is <c>49983-{id}.{Domain}</c>.</summary>
    public string Domain { get; set; } = "e2b.app";

    /// <summary>The template a sandbox starts from when the spec names none.</summary>
    public string Template { get; set; } = "base";

    /// <summary>
    /// The template a sandbox with a screen starts from: E2B's public <c>desktop</c> (Xvfb, an Xfce session, xdotool,
    /// scrot). It is a far larger machine than <see cref="Template"/>.
    /// </summary>
    public string DesktopTemplate { get; set; } = "desktop";

    #endregion

    #region Public Methods

    /// <summary>Whether the text has the shape of an E2B key. E2B refuses any other as malformed.</summary>
    public static bool IsWellFormedKey(string key) => key.StartsWith("e2b_", StringComparison.Ordinal) && key.Length > 4;

    #endregion
}
