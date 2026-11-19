namespace Drover.Presentation.Tests.Support;

/// <summary>A desktop updater controlled by a view-model test.</summary>
public sealed class MemoryDesktopUpdates(DesktopUpdateState state) : IDesktopUpdates
{
    #region Properties

    public DesktopUpdateState State { get; private set; } = state;

    public int Checks { get; private set; }

    public int Installs { get; private set; }

    #endregion

    #region Interface Implementations

    public event Action? Changed;

    public Task Start() => Task.CompletedTask;

    public void SetChecks(bool enabled) => Change(State with { ChecksEnabled = enabled });

    public Task Check()
    {
        Checks++;
        return Task.CompletedTask;
    }

    public Task Install()
    {
        Installs++;
        return Task.CompletedTask;
    }

    #endregion

    #region Public Methods

    public void Change(DesktopUpdateState value)
    {
        State = value;
        Changed?.Invoke();
    }

    #endregion
}
