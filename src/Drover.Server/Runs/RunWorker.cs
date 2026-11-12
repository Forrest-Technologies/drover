namespace Drover.Server.Runs;

/// <summary>Runs the engine's dispatcher for as long as the server is up. All of the work is in <c>Drover.Engine</c>.</summary>
public sealed class RunWorker(RunDispatcher dispatcher) : BackgroundService
{
    #region Protected Methods

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => dispatcher.Run(stoppingToken);

    #endregion
}
