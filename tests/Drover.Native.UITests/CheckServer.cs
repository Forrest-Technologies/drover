namespace Drover.Native.UITests;

/// <summary>
/// The real server, in process, listening on a loopback port over HTTPS with a certificate made for this run, over
/// a database of its own. A scripted model answers every message with <see cref="Reply"/>, so a conversation runs
/// through the real engine with no network.
/// </summary>
internal sealed class CheckServer(TestDatabase database, X509Certificate2 certificate, string folder)
    : WebApplicationFactory<global::Program>
{
    #region Private Fields

    public const string Reply = "Hello from the check bot.";

    // The app sends one message and gets its reply. The second turn holds its run in progress until the run is
    // stopped, so the composer shows Stop for as long as a check needs to look at it. The rest are spare, in case
    // the engine asks the model for anything else.
    private static readonly ScriptedModel script = new(
    [
        [new TextOutput(Reply)],
        [new PauseOutput()],
        .. Enumerable.Repeat<IReadOnlyList<ModelOutput>>([new TextOutput(Reply)], 8),
    ]);

    #endregion

    #region Properties

    /// <summary>The address the app connects to.</summary>
    public Uri Origin => new(Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses
        .Select(address => new Uri(address))
        .Select(address => $"https://127.0.0.1:{address.Port}/")
        .First());

    public string SetupCode => Services.GetRequiredService<SetupCode>().Display;

    #endregion

    #region Public Methods

    /// <summary>Starts listening on a free loopback port.</summary>
    public void Start()
    {
        UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        StartServer();
    }

    #endregion

    #region Overrides

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The app talks only to the API, so the server's own files are the ones built beside the harness.
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseSetting("ConnectionStrings:Drover", database.AppConnectionString);
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(folder, "keys"));
        builder.UseSetting("Files:Path", Path.Combine(folder, "files"));
        builder.UseSetting("Drover:SignInRateLimit:PermitLimit", "1000");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureServices(services =>
            services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(script)));
    }

    #endregion
}
