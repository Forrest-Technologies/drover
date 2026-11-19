namespace Drover.Native.UITests;

/// <summary>
/// The installed Android app on the one device adb sees. The test server's port is reversed onto the device, so
/// the app reaches it at the same loopback address. The settings go in the extras of the intent that starts the
/// app, and its report comes back through the device log.
/// </summary>
internal sealed class AndroidApp : IAppUnderCheck
{
    #region Private Fields

    private const string Package = "com.forresttechnologies.drover.checks";

    private const string Tag = "DroverUiCheck";

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        var serial = await ChooseDevice();
        var port = new Uri(settings["DROVER_UI_CHECK_SERVER"]).Port;
        await Adb(serial, "reverse", $"tcp:{port}", $"tcp:{port}");

        try
        {
            await Adb(serial, "shell", "am", "force-stop", Package);
            await Adb(serial, "shell", "pm", "clear", Package);
            var started = long.Parse(
                (await Adb(serial, "shell", "date", "+%s")).Trim(),
                System.Globalization.CultureInfo.InvariantCulture);
            var activity = (await Adb(serial, "shell", "cmd", "package", "resolve-activity", "--brief", Package))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
            string[] start = ["shell", "am", "start", "-W", "-n", activity];
            await Adb(serial, [.. start, .. settings.SelectMany(setting => new[] { "--es", setting.Key, $"'{setting.Value}'" })]);
            var lines = await ReadReport(serial, started);

            if (!lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)))
            {
                logger.LogError("The device log, last lines:\n{Log}", string.Join('\n', (await Adb(serial, "logcat", "-d")).Split('\n').TakeLast(300)));
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

    private static async Task<string> ChooseDevice()
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

    private static async Task<IReadOnlyList<string>> ReadReport(string serial, long startedEpochSeconds)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var lines = AndroidCheckGuard.ReportsSince(
                await Adb(serial, "logcat", "-d", "-v", "epoch", "-s", $"{Tag}:I"),
                startedEpochSeconds).ToList();

            if (lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)) || watch.Elapsed > patience)
            {
                return lines;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static Task<string> Adb(string? serial, params string[] arguments)
    {
        var command = serial is null ? arguments : ["-s", serial, .. arguments];
        return Adb(command);
    }

    private static async Task<string> Adb(params string[] arguments)
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
            // The command, without the settings it carried: they hold the test account's password.
            throw new InvalidOperationException($"adb {string.Join(' ', arguments.Take(4))} failed: {await error}");
        }

        return await output;
    }

    #endregion
}
