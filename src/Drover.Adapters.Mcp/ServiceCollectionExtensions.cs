namespace Drover.Adapters.Mcp;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the tool source for remote tool servers. Its HTTP client is the checked outbound client every
    /// adapter gets, so the rule on private addresses applies to a tool server as to a model. A call is never
    /// repeated by the client: a tool may have acted already. Each server gets its own circuit, so one dead
    /// server pauses calls to itself only. Every answer is read through <see cref="ResponseLimitHandler"/>, so a
    /// server cannot make this one hold more than the limit the caller states.
    /// </summary>
    public static IServiceCollection AddMcpToolSource(this IServiceCollection services)
    {
        services.AddHttpClient(McpToolSource.HttpClientName)
            .AddHttpMessageHandler(() => new ResponseLimitHandler())
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
            })
            .SelectPipelineByAuthority();

        services.AddSingleton<McpToolSource>();
        services.AddSingleton<IToolSource>(provider => provider.GetRequiredService<McpToolSource>());
        return services;
    }

    #endregion
}
