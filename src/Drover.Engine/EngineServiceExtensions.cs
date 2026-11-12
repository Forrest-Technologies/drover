namespace Drover.Engine;

public static class EngineServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers Identity on Drover's own tables and the services that use it. Passwords use Identity's default
    /// hasher; an account locks after five wrong passwords for five minutes.
    /// </summary>
    public static IServiceCollection AddDroverEngine(this IServiceCollection services)
    {
        services
            .AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddDroverStores();
        services.AddScoped<AccountService>();
        services.AddScoped<SpaceService>();
        return services;
    }

    /// <summary>
    /// Registers the run engine: bots, threads, runs, the built-in tools, and the dispatcher that executes runs.
    /// Needs <c>AddDroverData</c>, an <see cref="IAgentRuntime"/>, the model providers, the secret vault, the
    /// deployment policy, and a <see cref="TimeProvider"/>. The host starts <see cref="RunDispatcher.Run"/>.
    /// </summary>
    public static IServiceCollection AddDroverRuns(this IServiceCollection services, Action<RunOptions>? configure = null)
    {
        services.AddOptions<RunOptions>().Configure(options => configure?.Invoke(options));
        services.AddBuiltinTools();
        services.AddSingleton<RunScopes>();
        services.AddSingleton<RunSignal>();
        services.AddSingleton<RunEventHub>();
        services.AddSingleton<RunExecutor>();
        services.AddSingleton<RunDispatcher>();
        services.AddScoped<ToolCatalog>();
        services.AddScoped<BotService>();
        services.AddScoped<ThreadService>();
        services.AddScoped<RunService>();
        return services;
    }

    #endregion
}
