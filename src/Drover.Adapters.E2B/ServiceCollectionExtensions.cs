namespace Drover.Adapters.E2B;

public static class ServiceCollectionExtensions
{
    #region Private Fields

    private const long MaxControlPlaneAnswerBytes = 1024 * 1024;

    private const long MaxDaemonAnswerBytes = 16 * 1024 * 1024;

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers the E2B sandbox provider. The control plane gets the standard resilience handler, widened because
    /// creating and resuming a sandbox takes seconds. The daemon's client gets none: a command's output streams for as
    /// long as the command runs, so the daemon client keeps its own limits per call and never retries a started command.
    /// </summary>
    public static IServiceCollection AddE2BSandboxProvider(this IServiceCollection services, Action<E2BOptions> configure)
    {
        services.AddOptions<E2BOptions>().Configure(configure);

        // The resilience handler's limits govern; the client's own 100-second default would cut across its retries.
        services.AddHttpClient(E2BSandboxProvider.ApiClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = MaxControlPlaneAnswerBytes;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(1);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                options.Retry.ShouldHandle = arguments => ValueTask.FromResult(RetryRules.ShouldRetry(arguments.Outcome));
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            });

        services.AddHttpClient(E2BSandboxProvider.EnvdClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.MaxResponseContentBufferSize = MaxDaemonAnswerBytes;
        });

        services.AddSingleton<ISandboxProvider, E2BSandboxProvider>();

        return services;
    }

    #endregion
}
