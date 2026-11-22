namespace Drover.Domain.Sandboxes;

/// <summary>
/// A sandbox secret a sandbox was started with. A brokered one is known to the sandbox only by its placeholder, kept
/// here as a hash; a raw one is in the sandbox as it is, so the row only says it is there, for taking the sandbox out of
/// use when the value is removed or given to fewer bots.
/// </summary>
public sealed class SandboxSecretEntry : ISpaceOwned
{
    #region Properties

    public Guid SpaceId { get; set; }

    public Guid SandboxId { get; set; }

    public Guid SandboxSecretId { get; set; }

    /// <summary>A SHA-256 hash of the placeholder, for a brokered secret; null for a raw one.</summary>
    public byte[]? PlaceholderHash { get; set; }

    #endregion
}
