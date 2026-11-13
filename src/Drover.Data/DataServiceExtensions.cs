namespace Drover.Data;

public static class DataServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Registers the context for serving requests and jobs. The connection string must name the application role,
    /// <c>drover_app</c>. The context is scoped, not pooled: a pooled context shares its interceptors across
    /// scopes, and these interceptors belong to one scope's <see cref="TenantContext"/>. Connections are still
    /// pooled by Npgsql.
    /// </summary>
    public static IServiceCollection AddDroverData(this IServiceCollection services, string appConnectionString)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<TenantScope>();
        services.AddScoped<TenantTransactionInterceptor>();
        services.AddScoped<TenantGuardInterceptor>();
        services.AddScoped<SpaceStampInterceptor>();
        services.AddScoped<AccountRepository>();
        services.AddScoped<SecretRepository>();
        services.AddScoped<ModelConnectionRepository>();
        services.AddScoped<MessageRepository>();
        services.AddScoped<AgentSessionRepository>();
        services.AddScoped<ToolCallRepository>();
        services.AddScoped<WorkflowCheckpointRepository>();
        services.AddScoped<RunRepository>();
        services.AddScoped<RunEventRepository>();
        services.AddScoped<ThreadRepository>();
        services.AddScoped<BotRepository>();
        services.AddScoped<ToolRepository>();
        services.AddScoped<BotScheduleRepository>();
        services.AddScoped<AuditRepository>();
        services.AddSingleton(provider => new RunWakeListener(appConnectionString, provider.GetRequiredService<ILogger<RunWakeListener>>()));
        services.AddDbContext<DroverDbContext>((provider, options) => options
            .UseDroverNpgsql(appConnectionString)
            .AddInterceptors(
                provider.GetRequiredService<TenantTransactionInterceptor>(),
                provider.GetRequiredService<TenantGuardInterceptor>(),
                provider.GetRequiredService<SpaceStampInterceptor>()));
        return services;
    }

    /// <summary>Stores Identity's users in <see cref="DroverDbContext"/>, so the engine need not name the context.</summary>
    public static IdentityBuilder AddDroverStores(this IdentityBuilder builder) =>
        builder.AddEntityFrameworkStores<DroverDbContext>();

    /// <summary>The provider settings every Drover context shares: PostgreSQL, snake_case names, no retries.</summary>
    public static DbContextOptionsBuilder UseDroverNpgsql(this DbContextOptionsBuilder options, string connectionString)
    {
        // The EF Core retrying execution strategy stays off, which is Npgsql's default: do not call
        // EnableRetryOnFailure. It rejects a transaction the caller started unless the caller wraps it, and it
        // replays the whole delegate, which would run a tenant transaction twice. Retries belong at the job
        // level, where the run engine already has them.
        return options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
    }

    #endregion
}
