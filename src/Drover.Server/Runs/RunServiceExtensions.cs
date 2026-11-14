namespace Drover.Server.Runs;

public static class RunServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the run engine, the agent runtime it drives, and the worker that executes runs. The runtime talks
    /// to the model client the engine built for the run, so every model call is counted and policy-checked by the
    /// engine. Remote tool servers are reached through the tool source registered here. Settings under <c>Runs</c> change the pace: <c>Runs:Workers</c>, <c>Runs:PollSeconds</c>,
    /// <c>Runs:StepLimit</c>, <c>Runs:MaxToolOutputCharacters</c>.
    /// </summary>
    public static IServiceCollection AddDroverRunEngine(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDroverRuns(options =>
        {
            var section = configuration.GetSection("Runs");
            options.Workers = section.GetValue("Workers", options.Workers);
            options.StepLimit = section.GetValue("StepLimit", options.StepLimit);
            options.MaxToolOutputCharacters = section.GetValue("MaxToolOutputCharacters", options.MaxToolOutputCharacters);
            options.PollInterval = TimeSpan.FromSeconds(section.GetValue("PollSeconds", options.PollInterval.TotalSeconds));
            options.HeartbeatInterval = TimeSpan.FromSeconds(section.GetValue("HeartbeatSeconds", options.HeartbeatInterval.TotalSeconds));
        });
        services.AddDroverAgents(execution => new ModelChatClientBridge(
            execution.Model ?? throw new InvalidOperationException("The engine starts every run with its model client.")));
        services.AddMcpToolSource();

        // Signing in to a tool server sends the person's browser away and back, so this server must know its own
        // public address. Without it, connections that need a sign-in say what is missing.
        services.Configure<ToolSignInOptions>(options =>
        {
            options.PublicUrl = PublicAddress.Read(configuration);
        });
        services.AddHostedService<ToolSignInRenewer>();

        // A tool server is reached through the checked outbound client, and each request carries the space's rule
        // on private addresses. A request that says nothing is refused a private address.
        services.TryAddTransient<SpaceOutboundHandler>();
        // It never follows a redirect: a connection's header would go to whatever host the answer names.
        services.AddHttpClient(McpToolSource.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider =>
                OutboundHttp.CreateHandlerWithoutRedirects(provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules))
            .AddHttpMessageHandler<SpaceOutboundHandler>();
        services.AddHostedService<RunWorker>();
        return services;
    }

    #endregion
}
