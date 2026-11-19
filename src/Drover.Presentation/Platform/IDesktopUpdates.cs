namespace Drover.Presentation.Platform;

/// <summary>The state of this desktop installation's signed update check.</summary>
public sealed record DesktopUpdateState(
    bool Supported,
    bool ChecksEnabled,
    DesktopUpdatePhase Phase = DesktopUpdatePhase.Idle,
    string? Version = null,
    string? Problem = null,
    string? VerifiedPackage = null);


public enum DesktopUpdatePhase
{
    Idle,
    Checking,
    Available,
    Downloading,
    Installing,
    Failed,
    Manual,
}


/// <summary>A desktop installation checking and applying packages signed by Drover's update key.</summary>
public interface IDesktopUpdates
{
    event Action? Changed;

    DesktopUpdateState State { get; }

    Task Start();

    void SetChecks(bool enabled);

    Task Check();

    Task Install();
}


/// <summary>The update surface on a target that is not a packaged desktop installation.</summary>
public sealed class UnsupportedDesktopUpdates : IDesktopUpdates
{
    #region Properties

    public DesktopUpdateState State { get; } = new(false, false);

    #endregion

    #region Interface Implementations

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task Start() => Task.CompletedTask;

    public void SetChecks(bool enabled)
    {
    }

    public Task Check() => Task.CompletedTask;

    public Task Install() => Task.CompletedTask;

    #endregion
}


/// <summary>The release channel carried by an installed package.</summary>
public static class DesktopUpdateChannel
{
    public static string FromVersion(string version) => version.Contains('-', StringComparison.Ordinal) ? "preview" : "stable";
}
