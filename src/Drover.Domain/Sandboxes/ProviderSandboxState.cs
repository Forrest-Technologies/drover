namespace Drover.Domain.Sandboxes;

/// <summary>What the provider says about a sandbox. Kept apart from <see cref="SandboxState"/>, which is Drover's row.</summary>
public enum ProviderSandboxState
{
    Running,
    Paused,
    Gone,
}
