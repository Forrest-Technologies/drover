namespace Drover.Adapters.Mcp;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the tool source for remote tool servers. Its HTTP client is the checked outbound client every
    /// adapter gets, so the rule on private addresses applies to a tool server as to a model. A call is never
    /// repeated by the client: a tool may have acted already. Each connection gets its own circuit, so a tool
    /// server that stops answering pauses the calls of the connections that reach it and of nobody else: one
    /// space's trouble with a public tool server is not every space's. Every answer is read through <see cref="ResponseLimitHandler"/>, so a
    /// server cannot make this one hold more than the limit the caller states, and every request passes
    /// <see cref="CredentialGuardHandler"/>, the sign-in requests the protocol library makes included.
    /// </summary>
    public static IServiceCollection AddMcpToolSource(this IServiceCollection services)
    {
        services.AddHttpClient(McpToolSource.HttpClientName)
            .AddHttpMessageHandler(() => new ResponseLimitHandler())
            .AddHttpMessageHandler(() => new CredentialGuardHandler())
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
            })
            .SelectPipelineBy(_ => request => PipelineKey(request));

        services.AddSingleton<McpToolSource>();
        services.AddSingleton<IToolSource>(provider => provider.GetRequiredService<McpToolSource>());
        return services;
    }

    /// <summary>
    /// Which circuit a request belongs to: the connection it is made for, and the address it goes to. The key is
    /// shown in metrics and logs, so it holds no credential and no name a person chose: a connection's id is an
    /// opaque id that logs already carry. A request made outside any use of a connection, which there should be
    /// none of, is keyed by its address alone.
    /// </summary>
    private static string PipelineKey(HttpRequestMessage request)
    {
        var authority = request.RequestUri is { } address ? address.GetLeftPart(UriPartial.Authority) : "unknown";
        return SignInAttempt.Current is { } use ? $"{authority}|{use.Endpoint.ConnectionId}" : authority;
    }

    #endregion
}
