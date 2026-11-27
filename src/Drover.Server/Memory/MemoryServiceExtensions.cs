namespace Drover.Server.Memory;

public static class MemoryServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Configures <see cref="MemoryOptions"/> from <c>Drover:Memory</c>, validates them at startup, and registers the
    /// hourly forget retry sweeper when <see cref="DeploymentPolicy.AllowMemoryService"/> is on. When the deployment
    /// policy forbids memory services, no provider is registered and nothing is sent, whatever configuration says.
    /// </summary>
    public static IServiceCollection AddDroverMemory(
        this IServiceCollection services,
        IConfiguration configuration,
        DeploymentPolicy policy)
    {
        services.AddOptions<MemoryOptions>()
            .Configure(options =>
            {
                var section = configuration.GetSection(MemoryOptions.Section);
                var provider = section[nameof(MemoryOptions.Provider)]?.Trim();
                options.Provider = string.IsNullOrEmpty(provider) ? null : provider;
                options.RecallMaxCharacters = section.GetValue(nameof(MemoryOptions.RecallMaxCharacters), options.RecallMaxCharacters);
                options.RecallTimeoutSeconds = section.GetValue(nameof(MemoryOptions.RecallTimeoutSeconds), options.RecallTimeoutSeconds);
                options.RememberTimeoutSeconds = section.GetValue(nameof(MemoryOptions.RememberTimeoutSeconds), options.RememberTimeoutSeconds);
            })
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<MemoryOptions>, MemoryOptionsValidator>();

        if (!policy.AllowMemoryService)
        {
            return services;
        }

        services.AddHostedService<MemoryForgetSweeper>();
        return services;
    }

    #endregion
}
