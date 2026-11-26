namespace Drover.Client;

/// <summary>Extensions: what the extensions screen calls.</summary>
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

    /// <summary>
    /// Imports an extension file (admin only): the same text that was read for review. Its bot thinks with the model
    /// the preview named and starts paused; turn it on with <see cref="ResumeBot"/>.
    /// </summary>
    public Task<ExtensionSummary> ImportExtension(Guid spaceId, string package, CancellationToken cancellationToken = default) =>
        Post<ExtensionSummary>($"{Space(spaceId)}/extensions", new ExtensionText(package), cancellationToken);

    /// <summary>
    /// The file as its package (admin only), read by the server as strictly as an import reads it. The editor calls
    /// this to turn text back into its form, and to check a file. Text it cannot read is refused with a sentence.
    /// </summary>
    public Task<ExtensionPackage> ReadExtension(Guid spaceId, string package, CancellationToken cancellationToken = default) =>
        Post<ExtensionPackage>($"{Space(spaceId)}/extensions/read", new ExtensionText(package), cancellationToken);

    /// <summary>
    /// A bot written down as an extension (admin only), for the editor to open: its name, what it is for, its
    /// instructions, and its model as a suggestion. A bot from an installed extension also keeps that file's id,
    /// description, and non-secret connection recipe.
    /// </summary>
    public Task<ExtensionPackage> ExportBotAsExtension(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<ExtensionPackage>($"{Space(spaceId)}/extensions/from-bot/{botId}", cancellationToken);

    /// <summary>
    /// Connects the tool server an installed extension names (admin only) and asks it for its tools. The address is
    /// the file's. A header value, where the server wants one, goes to the vault and is never returned.
    /// </summary>
    public Task<ExtensionSummary> ConnectExtension(Guid spaceId, Guid extensionId, ConnectExtensionRequest request, CancellationToken cancellationToken = default) =>
        Post<ExtensionSummary>($"{Space(spaceId)}/extensions/{extensionId}/connection", request, cancellationToken);

    #endregion
}
