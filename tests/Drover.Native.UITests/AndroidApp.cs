namespace Drover.Native.UITests;

/// <summary>
/// The installed Android app on the one device adb sees. The test server's port is reversed onto the device, so
/// the app reaches it at the same loopback address. The settings go in the extras of the intent that starts the
/// app, and its report comes back through the device log.
/// </summary>
internal sealed class AndroidApp : IAppUnderCheck
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    #endregion

    #region Properties

    public const string Package = "com.forresttechnologies.drover.checks";

    public const string Tag = "DroverUiCheck";

    #endregion

    #region Public Methods

    /// <summary>Starts the checks build from nothing on the device, with the settings in the start intent.</summary>
    public static async Task Launch(string serial, IReadOnlyDictionary<string, string> settings)
    {
        await Adb(serial, "shell", "am", "force-stop", Package);
        await Adb(serial, "shell", "pm", "clear", Package);
        var activity = (await Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", Package))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        string[] start = ["shell", "am", "start", "-W", "-n", activity];
        string[] extras = [.. settings.SelectMany(setting => (string[])["--es", setting.Key, $"'{setting.Value}'"])];

        // The extras carry the test account's password, which the device's error text may repeat, so a failure here
        // says only that it failed.
        await AdbWithheld(serial, [.. start, .. extras]);
    }

    public static async Task<string> ChooseDevice()
    {
        var serials = AndroidCheckGuard.Serials(await Adb(null, "devices"));
        var selected = Environment.GetEnvironmentVariable("DROVER_ANDROID_SERIAL");
        string? version = null;
        if (!string.IsNullOrWhiteSpace(selected) && serials.Contains(selected))
        {
            version = AndroidCheckGuard.VersionName(await Adb(selected, "shell", "dumpsys", "package", Package));
        }

        return AndroidCheckGuard.Choose(serials, selected, version)
            ?? throw new InvalidOperationException(
                "Refusing to clear a package that was not named for this run, or that is not the checks build.");
    }

    public static Task<string> Adb(string? serial, params string[] arguments)
    {
        var command = serial is null ? arguments : ["-s", serial, .. arguments];
        return Adb(command);
    }

    public static Task<string> Adb(params string[] arguments) => Run(arguments, withhold: false);

    /// <summary>
    /// Runs adb for a command whose arguments hold a secret. A failure is reported without the device's error text,
    /// which can repeat the arguments, so the harness log that is published cannot carry the secret.
    /// </summary>
    public static Task<string> AdbWithheld(string serial, params string[] arguments) =>
        Run(["-s", serial, .. arguments], withhold: true);

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        var serial = await ChooseDevice();
        var port = new Uri(settings["DROVER_UI_CHECK_SERVER"]).Port;
        var runId = settings.GetValueOrDefault("DROVER_UI_CHECK_RUN") ?? Guid.NewGuid().ToString("N");
        await Adb(serial, "reverse", $"tcp:{port}", $"tcp:{port}");

        try
        {
            await Launch(serial, settings);
            var lines = await ReadReport(serial, runId);

            if (!lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)))
            {
                var tagLog = await Adb(serial, "logcat", "-d", "-v", "epoch", "-s", $"{Tag}:*");
                var filtered = AndroidCheckGuard.TagLogsForRun(tagLog, runId, Tag);
                logger.LogError("The {Tag} log for this run:\n{Log}", Tag, string.Join('\n', filtered.TakeLast(300)));
            }

            return lines;
        }
        finally
        {
            await Adb(serial, "shell", "am", "force-stop", Package);
            await Adb(serial, "reverse", "--remove", $"tcp:{port}");
        }
    }

    #endregion

    #region Private Methods

    private static async Task<string> Run(string[] arguments, bool withhold)
    {
        var start = new ProcessStartInfo("adb") { RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var adb = Process.Start(start) ?? throw new InvalidOperationException("adb did not start.");
        var output = adb.StandardOutput.ReadToEndAsync();
        var error = adb.StandardError.ReadToEndAsync();
        await adb.WaitForExitAsync();

        if (adb.ExitCode != 0)
        {
            throw new InvalidOperationException(
                withhold
                    ? $"adb {arguments[2]} {arguments[3]} failed with exit code {adb.ExitCode}; its output is withheld."
                    : $"adb {string.Join(' ', arguments.Take(4))} failed: {await error}");
        }

        return await output;
    }

    private static async Task<IReadOnlyList<string>> ReadReport(string serial, string runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var lines = AndroidCheckGuard.ReportsForRun(
                await Adb(serial, "logcat", "-d", "-v", "epoch", "-s", $"{Tag}:I"),
                runId).ToList();

            if (lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)) || watch.Elapsed > patience)
            {
                return lines;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    #endregion
}
