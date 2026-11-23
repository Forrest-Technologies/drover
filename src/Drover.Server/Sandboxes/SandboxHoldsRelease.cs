namespace Drover.Server.Sandboxes;

/// <summary>
/// Lets go of every mouse button a person still holds on a sandbox screen when the server stops. It runs in the host's
/// stop, while a release can still open its tenant scope; disposing the services later is too late, because the
/// service provider then refuses new scopes. The wait is bounded by ten seconds and by the host's own shutdown time,
/// and a button still held when it ends is logged by sandbox. All of the work is in <c>Drover.Engine</c>.
/// </summary>
public sealed class SandboxHoldsRelease(SandboxControlService sandboxes) : IHostedService
{
    #region Interface Implementations

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => sandboxes.LetGoOfEverything(cancellationToken);

    #endregion
}
