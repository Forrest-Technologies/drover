namespace Drover.Native.UITests;

/// <summary>
/// Runs the native screen checks against a build of the app made for them:
/// <c>dotnet run --project tests/Drover.Native.UITests -- desktop path/to/Drover.Native.dll</c>, or
/// <c>-- android</c> for the build installed on the one Android device, or <c>-- ios</c> with an app and simulator.
/// <c>-- android-proof folder</c> is the manual Android proof: the same device, a sandbox that records its input, and
/// the folder its artifacts are kept in.
/// It starts PostgreSQL (the server named by
/// <c>DROVER_TEST_POSTGRES</c>, or a container), the real server on a loopback HTTPS port, and an account with one
/// bot, then the app, and exits with 0 only when the app reports that every check passed.
/// </summary>
internal static class Program
{
    #region Private Fields

    public const string ResultPrefix = "ui-check result:";

    private const string Passed = "ui-check result: passed";

    #endregion

    #region Public Methods

    public static async Task<int> Main(string[] args)
    {
        using var logging = LoggerFactory.Create(builder => builder.AddSimpleConsole(console => console.SingleLine = true));
        var logger = logging.CreateLogger("Drover.Native.UITests");
        var folder = Directory.CreateTempSubdirectory("drover-ui-checks-").FullName;

        if (args is ["android-proof", var artifacts])
        {
            try
            {
                return await Prove(Path.GetFullPath(artifacts), folder, logger) ? 0 : 1;
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        if (Target(args, folder) is not { } app)
        {
            logger.LogError(
                "Name the app to check: desktop <path to Drover.Native.dll>, android, android-proof <folder>, or ios <app> <device>");
            return 2;
        }

        try
        {
            var lines = await Check(app, folder, logger);

            foreach (var line in lines)
            {
                logger.LogInformation("{Line}", line);
            }

            return lines.Contains(Passed) ? 0 : 1;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    #endregion

    #region Private Methods

    private static IAppUnderCheck? Target(string[] args, string folder) => args switch
    {
        ["desktop", var path] => new DesktopApp(Path.GetFullPath(path), folder),
        ["android"] => new AndroidApp(),
        ["ios", var path, var device] => new IosApp(Path.GetFullPath(path), device),
        _ => null,
    };

    private static async Task<PostgresServer> StartPostgres()
    {
        var postgres = new PostgresServer();
        await postgres.InitializeAsync();
        return postgres;
    }

    private static async Task<bool> Prove(string artifacts, string folder, ILogger logger)
    {
        await using var postgres = await StartPostgres();
        await using var database = await postgres.CreateDatabase();
        using var certificate = TestCertificate.Create();
        var sandbox = new MemorySandboxProvider { Screen = ProofImages.Screen() };
        await using var server = new CheckServer(database, certificate, folder, sandbox);

        server.Start();
        await CheckAccount.WaitUntilReady(server, certificate, logger);
        var account = await CheckAccount.Create(server, certificate, desktop: true);
        logger.LogInformation("The test server is at {Origin}; starting the app", server.Origin);

        return await new AndroidProof(logger, artifacts).Run(account, sandbox, ProofImages.Attachment(), new Dictionary<string, string>
        {
            ["DROVER_UI_CHECK_SERVER"] = server.Origin.AbsoluteUri,
            ["DROVER_UI_CHECK_EMAIL"] = account.Email,
            ["DROVER_UI_CHECK_PASSWORD"] = account.Password,
            ["DROVER_UI_CHECK_CERTIFICATE"] = TestCertificate.Hash(certificate),
            ["DROVER_UI_CHECK_REPLY"] = CheckServer.Reply,
            ["DROVER_UI_CHECK_RUN"] = Guid.NewGuid().ToString("N"),
            ["DROVER_UI_CHECK_PROOF_PICTURE"] = AndroidProof.PictureName,
            ["DROVER_UI_CHECK_PROOF_BOT"] = account.BotId.ToString(),
        });
    }

    private static async Task<IReadOnlyList<string>> Check(IAppUnderCheck app, string folder, ILogger logger)
    {
        await using var postgres = await StartPostgres();
        await using var database = await postgres.CreateDatabase();
        using var certificate = TestCertificate.Create();
        await using var server = new CheckServer(database, certificate, folder);

        server.Start();
        var account = await CheckAccount.Create(server, certificate);
        logger.LogInformation("The test server is at {Origin}; starting the app", server.Origin);

        return await app.Run(new Dictionary<string, string>
        {
            ["DROVER_UI_CHECK_SERVER"] = server.Origin.AbsoluteUri,
            ["DROVER_UI_CHECK_EMAIL"] = account.Email,
            ["DROVER_UI_CHECK_PASSWORD"] = account.Password,
            ["DROVER_UI_CHECK_CERTIFICATE"] = TestCertificate.Hash(certificate),
            ["DROVER_UI_CHECK_REPLY"] = CheckServer.Reply,
            ["DROVER_UI_CHECK_RUN"] = Guid.NewGuid().ToString("N"),
        }, logger);
    }

    #endregion
}
