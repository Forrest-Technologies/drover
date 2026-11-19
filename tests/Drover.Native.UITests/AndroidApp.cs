namespace Drover.Native.UITests;

/// <summary>
/// The installed Android app on the one device adb sees. The test server's port is reversed onto the device, so
/// the app reaches it at the same loopback address. The settings go in the extras of the intent that starts the
/// app, and its report comes back through the device log.
/// </summary>
internal sealed class AndroidApp : IAppUnderCheck
{
    #region Private Fields

    private const string Package = "com.forresttechnologies.drover.client";

    private const string Tag = "DroverUiCheck";

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        var port = new Uri(settings["DROVER_UI_CHECK_SERVER"]).Port;
        await Adb("reverse", $"tcp:{port}", $"tcp:{port}");

        try
        {
            await Adb("shell", "am", "force-stop", Package);
            await Adb("shell", "pm", "clear", Package);
            await Adb("logcat", "-c");
            var activity = (await Adb("shell", "cmd", "package", "resolve-activity", "--brief", Package)).Split('\n')[^1].Trim();
            string[] start = ["shell", "am", "start", "-W", "-n", activity];
            await Adb([.. start, .. settings.SelectMany(setting => new[] { "--es", setting.Key, $"'{setting.Value}'" })]);
            var lines = await ReadReport();

            if (!lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)))
            {
                logger.LogError("The device log, last lines:\n{Log}", string.Join('\n', (await Adb("logcat", "-d")).Split('\n').TakeLast(300)));
            }

            return lines;
        }
        finally
        {
            await Adb("shell", "am", "force-stop", Package);
            await Adb("reverse", "--remove", $"tcp:{port}");
        }
    }

    #endregion

    #region Private Methods

    private static async Task<IReadOnlyList<string>> ReadReport()
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var lines = (await Adb("logcat", "-d", "-v", "raw", "-s", $"{Tag}:I"))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("ui-check", StringComparison.Ordinal))
                .ToList();

            if (lines.Any(line => line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal)) || watch.Elapsed > patience)
            {
                return lines;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
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
            throw new InvalidOperationException($"adb {string.Join(' ', arguments)} failed: {await error}");
        }

        return await output;
    }

    #endregion
}
