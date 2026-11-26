namespace Drover.Client;

/// <summary>Extensions: what the extensions screen and the appearance choices call.</summary>
public sealed partial class DroverApi
{
    #region Extensions

    /// <summary>The extensions installed in the space, the newest first. Any member may read them.</summary>
    public Task<List<ExtensionSummary>> ListExtensions(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<ExtensionSummary>>($"{Space(spaceId)}/extensions", cancellationToken);

    /// <summary>
    /// What an extension file would add (admin only), for a person to read before importing it. The server reads
    /// the file strictly and contacts nothing. A file it cannot read is refused with a sentence that says why.
    /// </summary>
    public Task<ExtensionPreview> PreviewExtension(Guid spaceId, string package, CancellationToken cancellationToken = default) =>
        Post<ExtensionPreview>($"{Space(spaceId)}/extensions/preview", new ExtensionText(package), cancellationToken);

    /// <summary>Imports an extension file (admin only): the same text that was read for review. Nothing is contacted and nothing is given to a bot.</summary>
    public Task<ExtensionSummary> ImportExtension(Guid spaceId, string package, CancellationToken cancellationToken = default) =>
        Post<ExtensionSummary>($"{Space(spaceId)}/extensions", new ExtensionText(package), cancellationToken);

    /// <summary>
    /// Connects the tool server an installed extension names (admin only) and asks it for its tools. The address is
    /// the file's, with the settings filled in. A secret setting or header value goes to the vault and is never returned.
    /// </summary>
    public Task<ExtensionSummary> ConnectExtension(Guid spaceId, Guid extensionId, ConnectExtensionRequest request, CancellationToken cancellationToken = default) =>
        Post<ExtensionSummary>($"{Space(spaceId)}/extensions/{extensionId}/connection", request, cancellationToken);

    /// <summary>Removes an installed extension (admin only), with the tool connection it made.</summary>
    public Task RemoveExtension(Guid spaceId, Guid extensionId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/extensions/{extensionId}", cancellationToken);

    /// <summary>The themes the space's extensions offer its members, each as a theme file's text. Any member may read them.</summary>
    public Task<List<SpaceTheme>> ListSpaceThemes(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<SpaceTheme>>($"{Space(spaceId)}/themes", cancellationToken);

    #endregion
}
