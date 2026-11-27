namespace Drover.Adapters.OpenAICompatible;

public static class ServiceCollectionExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the compatible-endpoint provider. Streaming replies and reasoning models run for minutes, so the
    /// timeouts are wider than the resilience defaults. Each endpoint gets its own pipeline, so one failing server
    /// cannot pause calls to another. Model calls are not repeated after a timeout or a server error, because the
    /// provider may already have run, and billed, them: see <see cref="RetryRules"/>.
    /// </summary>
    public static IServiceCollection AddOpenAICompatibleProvider(this IServiceCollection services)
    {
        services.AddModelHttpClient(OpenAICompatibleProvider.HttpClientName);
        services.AddHttpClient(OpenAICompatibleProvider.RealtimeHttpClientName);

        services.AddSingleton<OpenAICompatibleProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<OpenAICompatibleProvider>());
        services.AddSingleton<IModelImageGenerator>(provider => provider.GetRequiredService<OpenAICompatibleProvider>());
        services.AddSingleton<ISpeechToText>(provider => provider.GetRequiredService<OpenAICompatibleProvider>());
        services.AddSingleton<ITextToSpeech>(provider => provider.GetRequiredService<OpenAICompatibleProvider>());
        services.AddSingleton<IRealtimeVoice>(provider => provider.GetRequiredService<OpenAICompatibleProvider>());
        services.AddSingleton<CompatibleChatClientBuilder>(provider => provider.GetRequiredService<OpenAICompatibleProvider>().CreateProtocolClient);
        services.AddSingleton<CompatibleImageGenerator>(provider => provider.GetRequiredService<OpenAICompatibleProvider>().GenerateCompatibleImage);

        return services;
    }

    /// <summary>
    /// Registers OpenAI on its Responses API, with its own named client and pipeline, under the same rules as the
    /// compatible client: wide timeouts, and a model call repeated only when the provider certainly did not run it.
    /// </summary>
    public static IServiceCollection AddOpenAIProvider(this IServiceCollection services)
    {
        services.AddModelHttpClient(OpenAIProvider.HttpClientName);
        services.AddSingleton<OpenAIProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<OpenAIProvider>());

        return services;
    }

    /// <summary>
    /// Registers the server's own speech service and returns its client's builder, so the server can give it the
    /// handler that marks its requests as going to an address the operator configured. A first sound from a speech
    /// model on a small machine can take a while, so the wait for the answer to begin is wider than the default.
    /// </summary>
    public static IHttpClientBuilder AddOperatorSpeech(this IServiceCollection services)
    {
        services.AddSingleton<OperatorSpeech>();
        services.AddSingleton<ISpeechToText>(provider => provider.GetRequiredService<OperatorSpeech>());
        services.AddSingleton<ITextToSpeech>(provider => provider.GetRequiredService<OperatorSpeech>());
        return services.AddHttpClient(OperatorSpeech.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Registers Copilot as a sign-in provider. Chat and model listing share the compatible transport; GitHub's
    /// device-code and token-exchange calls go through a second named client. Register the compatible transport
    /// first. The operator's <c>Drover:Models:Copilot</c> section is bound in <c>AddDroverModels</c>.
    /// </summary>
    public static IServiceCollection AddCopilotPlanProvider(this IServiceCollection services)
    {
        services.AddOptions<CopilotPlanOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddTransient<CopilotHeaderHandler>();

        services.AddHttpClient(CopilotPlanProvider.HttpClientName)
            .AddHttpMessageHandler<CopilotHeaderHandler>();
        services.AddModelHttpClient(CopilotPlanProvider.HttpClientName);
        services.AddHttpClient(CopilotPlanProvider.AuthHttpClientName)
            .AddHttpMessageHandler<CopilotHeaderHandler>();

        services.AddSingleton<CopilotPlanProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<CopilotPlanProvider>());
        services.AddSingleton<IModelSignIn, CopilotPlanSignIn>();
        return services;
    }

    #endregion

    #region Private Methods

    private static void AddModelHttpClient(this IServiceCollection services, string name) =>
        services.AddHttpClient(name)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.ShouldRetry(arguments.Context, arguments.Outcome));
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            })
            .SelectPipelineByAuthority();

    #endregion
}
