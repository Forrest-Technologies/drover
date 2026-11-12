namespace Drover.Server.Runs;

public static class RunServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the run engine, the agent runtime it drives, and the worker that executes runs. The runtime talks
    /// to the model client the engine built for the run, so every model call is counted and policy-checked by the
    /// engine. Settings under <c>Runs</c> change the pace: <c>Runs:Workers</c>, <c>Runs:PollSeconds</c>,
    /// <c>Runs:StepLimit</c>.
    /// </summary>
    public static IServiceCollection AddDroverRunEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDroverRuns(options =>
        {
            var section = configuration.GetSection("Runs");
            options.Workers = section.GetValue("Workers", options.Workers);
            options.StepLimit = section.GetValue("StepLimit", options.StepLimit);
            options.PollInterval = TimeSpan.FromSeconds(section.GetValue("PollSeconds", options.PollInterval.TotalSeconds));
            options.HeartbeatInterval = TimeSpan.FromSeconds(section.GetValue("HeartbeatSeconds", options.HeartbeatInterval.TotalSeconds));
        });
        services.AddDroverAgents(execution => new ModelChatClientBridge(
            execution.Model ?? throw new InvalidOperationException("The engine starts every run with its model client.")));
        services.AddHostedService<RunWorker>();
        return services;
    }

    #endregion
}
