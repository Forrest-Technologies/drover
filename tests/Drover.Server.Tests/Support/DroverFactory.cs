namespace Drover.Server.Tests.Support;

/// <summary>
/// The real server in process over a database of its own. It captures every log entry, and lets a request say which
/// address it came from (the in-process server has none) by sending <c>X-Test-Remote-Ip</c>.
/// </summary>
public sealed class DroverFactory(
    TestDatabase database,
    string keyRing,
    LogCapture logs,
    AdjustableTimeProvider time,
    HashCounter hashes,
    IReadOnlyDictionary<string, string?> settings,
    bool useAppConnection = true,
    Action<IServiceCollection>? configure = null,
    bool listsTestKinds = false) : WebApplicationFactory<Program>
{
    #region Public Methods

    /// <summary>Where the server under test keeps stored files: beside its key ring, removed with it.</summary>
    public static string FilesPath(string keyRing) => $"{keyRing}-files";

    #endregion

    #region Constants

    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>The kinds a hosted server under test offers when its test does not say.</summary>
    public static IReadOnlyList<string> HostedTestKinds { get; } =
        ["scripted", "scripted_model", "openai_compatible", "venice", FakeModelSignIn.PlanKind, FakeModelSignIn.OpenKind, OpenRouterSignIn.ConnectionKind];

    public const string AllowedKindsSetting = "Drover:Policy:Rules:AllowedModelProviderKinds";

    #endregion

    #region Overrides

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (useAppConnection)
        {
            builder.UseSetting("ConnectionStrings:Drover", database.AppConnectionString);
        }
        builder.UseSetting("DataProtection:KeyPath", keyRing);
        builder.UseSetting("Files:Path", FilesPath(keyRing));

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        if (listsTestKinds)
        {
            NameTestKindsAsAnOperatorWould(builder);
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(logs);
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, RemoteAddressFilter>();
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton(hashes);
            services.AddSingleton<IPasswordHasher<User>, CountingPasswordHasher>();
            configure?.Invoke(services);
        });
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The hosted preset offers no provider kind until its operator lists some, so a hosted server under test
    /// (started by ServerTestBase) lists the kinds the tests use, as an operator would, unless the test sets the list.
    /// </summary>
    private void NameTestKindsAsAnOperatorWould(IWebHostBuilder builder)
    {
        var listed = settings.Keys.Any(key => key.StartsWith(AllowedKindsSetting, StringComparison.Ordinal));

        if (listed || settings.GetValueOrDefault("DROVER_POLICY") != DeploymentPolicy.HostedName)
        {
            return;
        }

        for (var index = 0; index < HostedTestKinds.Count; index++)
        {
            builder.UseSetting($"{AllowedKindsSetting}:{index}", HostedTestKinds[index]);
        }
    }

    #endregion

    #region Nested Types

    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, pipeline) =>
                {
                    if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var address))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    }

                    await pipeline();
                });
                next(app);
            };
    }

    #endregion
}
