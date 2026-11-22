namespace Drover.Native.UITests;

/// <summary>
/// The iOS app installed in one simulator. The first launch runs the shared screen checks. The second launch proves
/// the server choice and sign-in survived the process restart in the system credential store.
/// </summary>
internal sealed class IosApp(string path, string device) : IAppUnderCheck
{
    #region Private Fields

    private const string Bundle = "com.forresttechnologies.drover.client";

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        await Simctl("install", device, path);

        try
        {
            var first = await Launch(settings, logger);
            if (!first.Contains("ui-check result: passed"))
            {
                return first;
            }

            var restart = settings.ToDictionary(pair => pair.Key, pair => pair.Value);
            restart["DROVER_UI_CHECK_RESTART"] = "true";
            var second = await Launch(restart, logger);
            if (!second.Contains("ui-check result: passed"))
            {
                var firstWithoutResult = first.Where(line =>
                    !line.StartsWith(IosCheckReport.ResultPrefix, StringComparison.Ordinal));
                return [.. firstWithoutResult, .. second];
            }

            return [.. first, .. second];
        }
        finally
        {
            await Terminate();
        }
    }

    #endregion

    #region Private Methods

    private async Task<IReadOnlyList<string>> Launch(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        var container = (await SimctlText("get_app_container", device, Bundle, "data")).Trim();
        var report = IosCheckReport.File(container);
        var directory = Path.GetDirectoryName(report);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(report))
        {
            File.Delete(report);
        }

        var child = new Dictionary<string, string>(settings)
        {
            ["DROVER_UI_CHECK_REPORT"] = report,
        };
        var start = new ProcessStartInfo("xcrun")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("simctl");
        start.ArgumentList.Add("launch");
        start.ArgumentList.Add("--terminate-running-process");
        start.ArgumentList.Add(device);
        start.ArgumentList.Add(Bundle);
        foreach (var (name, value) in child)
        {
            start.Environment[$"SIMCTL_CHILD_{name}"] = value;
        }

        using var launched = Process.Start(start) ?? throw new InvalidOperationException("The iOS app did not start.");
        var launchOutput = launched.StandardOutput.ReadToEndAsync();
        var launchError = launched.StandardError.ReadToEndAsync();
        await launched.WaitForExitAsync();
        await Task.WhenAll(launchOutput, launchError);
        if (launched.ExitCode != 0)
        {
            throw new InvalidOperationException($"simctl launch failed with exit code {launched.ExitCode}.");
        }

        var lines = new List<string>();
        string? outcome = null;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < patience)
        {
            if (File.Exists(report))
            {
                try
                {
                    lines = [.. IosCheckReport.Read(report)];
                    outcome = IosCheckReport.Result(lines);
                    if (outcome is not null)
                    {
                        break;
                    }
                }
                catch (IOException exception)
                {
                    logger.LogDebug(exception, "The iOS report file was busy.");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        await Terminate();
        if (outcome is null)
        {
            logger.LogError("The iOS app did not report a result within {Minutes} minutes", patience.TotalMinutes);
            outcome = "ui-check result: failed";
        }

        // The app's own log says why a check failed, such as a refused connection or a timeout. Without it a failure
        // like "Could not reach Drover" cannot be told from a server that was not ready.
        if (outcome != "ui-check result: passed")
        {
            var recent = await SimctlLogs();
            if (!string.IsNullOrWhiteSpace(recent))
            {
                logger.LogError("Recent iOS simulator logs for Drover.Native:{NewLine}{Logs}", Environment.NewLine, recent);
            }
        }

        return
        [
            .. lines.Where(line => !line.StartsWith(IosCheckReport.ResultPrefix, StringComparison.Ordinal)),
            outcome,
        ];
    }

    private async Task<string> SimctlLogs()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var start = new ProcessStartInfo("xcrun") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("simctl");
            start.ArgumentList.Add("spawn");
            start.ArgumentList.Add(device);
            start.ArgumentList.Add("log");
            start.ArgumentList.Add("show");
            start.ArgumentList.Add("--last");
            start.ArgumentList.Add("5m");
            start.ArgumentList.Add("--predicate");
            start.ArgumentList.Add("process == \"Drover.Native\"");
            start.ArgumentList.Add("--style");
            start.ArgumentList.Add("compact");

            using var process = Process.Start(start);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            var stderr = await error;
            var combined = string.IsNullOrWhiteSpace(text) ? stderr : text;
            var tail = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(80);
            return string.Join(Environment.NewLine, tail);
        }
        catch (Exception exception)
        {
            return $"Could not read simulator logs: {exception.Message}";
        }
    }

    private static async Task<string> SimctlText(params string[] arguments)
    {
        var start = new ProcessStartInfo("xcrun") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("simctl");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("simctl did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"simctl {arguments[0]} failed with exit code {process.ExitCode}.");
        }

        return await output;
    }

    private async Task Terminate()
    {
        await SimctlResult("terminate", device, Bundle);
    }

    private static async Task Simctl(params string[] arguments)
    {
        var result = await SimctlResult(arguments);
        if (result != 0)
        {
            throw new InvalidOperationException($"simctl {arguments[0]} failed with exit code {result}.");
        }
    }

    private static async Task<int> SimctlResult(params string[] arguments)
    {
        var start = new ProcessStartInfo("xcrun") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("simctl");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("simctl did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        return process.ExitCode;
    }

    #endregion
}
