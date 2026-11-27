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
    /// Registers OpenRouter's own sign-in and the provider that calls with the key it makes. The provider's client has
    /// the same wide timeouts and retry rules as the other model clients. The sign-in's client has no retry: its one
    /// request spends a one-time code. Both flows read <see cref="OpenRouterSignInOptions"/>, bound by the caller
    /// to <see cref="OpenRouterSignInOptions.Section"/>, and follow a change of it without a restart.
    /// </summary>
    public static IServiceCollection AddOpenRouterSignIn(this IServiceCollection services)
    {
        services.AddModelHttpClient(OpenRouterProvider.HttpClientName);
        services.AddHttpClient(OpenRouterSignIn.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddSingleton<OpenRouterProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<OpenRouterProvider>());
        services.AddSingleton<IModelSignIn, OpenRouterSignIn>();

        return services;
    }

    /// <summary>
    /// Registers the ChatGPT plan: its provider, on the Responses API with the person's own token, and its sign-in.
    /// The model calls have their own client and pipeline, under the same rules as OpenAI's except that a call the
    /// plan limited is not sent again: the plan's limit is the person's, and the provider asks apps to pause, not to
    /// repeat. The sign-in's client has no retries at all, because a code is spent once and a refresh token rotates.
    /// The server binds <see cref="OpenAIPlanOptions"/> from <see cref="OpenAIPlanOptions.Section"/>.
    /// </summary>
    public static IServiceCollection AddOpenAIPlanProvider(this IServiceCollection services)
    {
        services.AddModelHttpClient(OpenAIPlanProvider.HttpClientName, retryRateLimits: false);
        services.AddHttpClient(OpenAIPlanSignIn.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddOptions<OpenAIPlanOptions>();
        services.AddSingleton<OpenAIPlanProvider>();
        services.AddSingleton<IModelClientProvider>(provider => provider.GetRequiredService<OpenAIPlanProvider>());
        services.AddSingleton<OpenAIPlanSignIn>();
        services.AddSingleton<IModelSignIn>(provider => provider.GetRequiredService<OpenAIPlanSignIn>());

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

    #endregion

    #region Private Methods

    private static void AddModelHttpClient(this IServiceCollection services, string name, bool retryRateLimits = true) =>
        services.AddHttpClient(name)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);

                options.Retry.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.ShouldRetry(arguments.Context, arguments.Outcome, retryRateLimits));
                options.CircuitBreaker.ShouldHandle = arguments =>
                    ValueTask.FromResult(RetryRules.CountsAgainstCircuit(arguments.Outcome));
            })
            .SelectPipelineByAuthority();

    #endregion
}
