namespace Drover.Engine.Sandboxes;

/// <summary>
/// Whether this server has a sandbox provider. A server without one has no sandbox tools, and the bot editor and the
/// status page say so. Nothing here names the provider or holds its key.
/// </summary>
public sealed class SandboxAvailability(IEnumerable<ISandboxProvider> providers)
{
    #region Properties

    public bool IsConfigured { get; } = providers.Any();

    #endregion
}
