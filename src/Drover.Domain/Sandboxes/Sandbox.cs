namespace Drover.Domain.Sandboxes;

/// <summary>A sandbox owned by one bot. Access by other bots follows the sandbox policy.</summary>
public sealed class Sandbox : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid OwnerBotId { get; set; }

    public SandboxPolicy Policy { get; set; } = SandboxPolicy.Private;

    public required string Provider { get; set; }

    public required string ProviderSandboxId { get; set; }

    public SandboxState State { get; set; } = SandboxState.Creating;

    /// <summary>The per-sandbox token in the vault. A plain column: secrets have an audience and this row has none.</summary>
    public Guid? AccessSecretId { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RetireAfter { get; set; }

    #endregion
}
