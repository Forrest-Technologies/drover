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

    /// <summary>
    /// Not written and not read. Before an owner decision of 2026-11-20 it made every read from the sandbox taint the
    /// run, and a clean run's command ask. The column stays so the schema needs no migration; dropping it is a
    /// migration, which is for the owner to decide.
    /// </summary>
    public bool Tainted { get; set; }

    #endregion
}
