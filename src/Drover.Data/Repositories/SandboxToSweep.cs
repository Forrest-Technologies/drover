namespace Drover.Data.Repositories;

/// <summary>A sandbox the sweep must act on, as <c>sys.sandboxes_to_sweep</c> returns it: ids only.</summary>
public sealed record SandboxToSweep(Guid SpaceId, Guid SandboxId);
