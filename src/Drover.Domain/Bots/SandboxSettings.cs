namespace Drover.Domain.Bots;

public sealed record SandboxSettings
{
    public NetworkAccess Network { get; init; } = NetworkAccess.Off;

    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    public int? IdleTimeoutMinutes { get; init; }
}
