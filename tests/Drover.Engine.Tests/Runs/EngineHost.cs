namespace Drover.Engine.Tests.Runs;

/// <summary>
/// One server process for the run engine: its own service provider and its own dispatcher over the shared test
/// database, composed the way the server composes them, with the real agent runtime and a scripted model provider.
/// <see cref="Kill"/> abandons it without stopping anything first, so nothing it was doing can reach the database
/// again, exactly as if the process had died.
/// </summary>
public sealed class EngineHost : IAsyncDisposable
{
    #region Private Fields

    private readonly ServiceProvider provider;

    private readonly CancellationTokenSource stopping = new();

    private Task dispatching = Task.CompletedTask;

    private bool killed;

    #endregion

    #region Constructors

    public EngineHost(
        string appConnectionString,
        string keyRing,
        ScriptedModelProvider model,
        IEnumerable<BuiltinTool> tools,
        Action<RunOptions>? configure = null,
        Action<IServiceCollection>? replaceServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroverData(appConnectionString);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions()));
        services.AddScoped<PolicyService>();
        services.AddDroverSecretVault(keyRing);
        services.AddSingleton<IModelClientProvider>(model);
        services.AddScoped<ModelConnectionService>();
        services.AddDroverRuns(options =>
        {
            options.PollInterval = TimeSpan.FromMilliseconds(200);
            options.WatchInterval = TimeSpan.FromMilliseconds(200);
            configure?.Invoke(options);
        });

        foreach (var tool in tools)
        {
            services.AddSingleton(tool);
        }

        services.AddDroverAgents(execution => new ModelChatClientBridge(execution.Model!));
        replaceServices?.Invoke(services);
        provider = services.BuildServiceProvider();
    }

    #endregion

    #region Properties

    public IServiceProvider Services => provider;

    public string WorkerId => provider.GetRequiredService<RunDispatcher>().WorkerId;

    #endregion

    #region Public Methods

    /// <summary>Starts executing runs. A host that is only used to seed or read never calls this.</summary>
    public EngineHost Start()
    {
        dispatching = Task.Run(() => provider.GetRequiredService<RunDispatcher>().Run(stopping.Token));
        return this;
    }

    /// <summary>The process dies: its services vanish under whatever it was doing, and nothing is told to stop.</summary>
    public async Task Kill()
    {
        killed = true;
        await provider.DisposeAsync();
    }

    #endregion

    #region Interface Implementations

    /// <summary>A graceful stop, as the server does on shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        if (killed)
        {
            return;
        }

        killed = true;
        await stopping.CancelAsync();

        try
        {
            await dispatching.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            await provider.DisposeAsync();
            stopping.Dispose();
        }
    }

    #endregion
}
