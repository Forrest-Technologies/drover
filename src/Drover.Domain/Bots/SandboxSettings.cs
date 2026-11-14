namespace Drover.Domain.Bots;

public sealed record SandboxSettings
{
    #region Properties

    public NetworkAccess Network { get; init; } = NetworkAccess.Off;

    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    public int? IdleTimeoutMinutes { get; init; }

    /// <summary>
    /// The sandbox has a screen the bot can see and drive with the computer tools. It is chosen when the sandbox is
    /// made, so changing it replaces the bot's sandbox on its next use.
    /// </summary>
    public bool Desktop { get; init; }

    #endregion
}
