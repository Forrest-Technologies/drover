namespace Drover.Domain.Sandboxes;

/// <summary>What the broker asks a provider to create. The space and group are stored at the provider for the metadata check.</summary>
public sealed record SandboxSpec
{
    #region Properties

    public required Guid SpaceId { get; init; }

    /// <summary>The id of the bot that owns the sandbox. One provider sandbox serves one sharing group.</summary>
    public required Guid SharingGroupId { get; init; }

    /// <summary>The sandbox row's id, so a retried creation makes one sandbox, not two.</summary>
    public required Guid IdempotencyKey { get; init; }

    public required NetworkPolicy Network { get; init; }

    /// <summary>The provider's image or template name. Null uses the default the operator configured.</summary>
    public string? Image { get; init; }

    /// <summary>
    /// A sandbox with a screen: the provider's desktop image, with a display server and a desktop session installed.
    /// <see cref="Image"/> still wins when it is set.
    /// </summary>
    public bool Desktop { get; init; }

    /// <summary>How long the sandbox runs without a keep-alive before the provider pauses it.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    #endregion
}
