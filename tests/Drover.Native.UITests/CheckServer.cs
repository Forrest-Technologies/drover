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
    private readonly LogTail log = new();

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

    /// <summary>
    /// Asks the server's readiness endpoint over the same HTTPS connection the app will use, again and again with a
    /// growing wait, until it answers that the server and its database are ready. The wait is bounded by
    /// <paramref name="limit"/>. When it runs out the exception carries the last answer and the end of the server's
    /// log, so a server that never came up is reported with what it said. A server that is ready answers the first
    /// ask, so the wait costs one request.
    /// </summary>
    public async Task WaitUntilReady(TimeSpan limit, ILogger logger)
    {
        var hash = TestCertificate.Hash(certificate);
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                string.Equals(presented?.GetCertHashString(HashAlgorithmName.SHA256), hash, StringComparison.OrdinalIgnoreCase),
        };
        using var http = new HttpClient(handler) { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(5) };
        var watch = Stopwatch.StartNew();
        var delay = TimeSpan.FromMilliseconds(100);

        for (var attempt = 1; ; attempt++)
        {
            string answer;
            try
            {
                using var response = await http.GetAsync(new Uri("/health/ready", UriKind.Relative));
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation(
                        "The test server was ready after {Milliseconds} ms and {Attempts} attempt(s)", watch.ElapsedMilliseconds, attempt);
                    return;
                }

                answer = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                logger.LogDebug(exception, "The readiness request to the test server failed");
                answer = $"{exception.GetType().Name}: {exception.Message}";
            }

            if (watch.Elapsed + delay > limit)
            {
                throw new InvalidOperationException(
                    $"The test server at {Origin} was not ready after {watch.Elapsed.TotalSeconds:0} s and {attempt} attempts. The last answer was {answer}. The end of its log:{Environment.NewLine}{log.Text()}");
            }

            logger.LogWarning(
                "The test server was not ready (attempt {Attempt}: {Answer}); asking again in {DelayMilliseconds} ms",
                attempt, answer, delay.TotalMilliseconds);
            await Task.Delay(delay);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
        }
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
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddProvider(log);
        });
        builder.ConfigureServices(services =>
            services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(script)));
    }

    #endregion
}
