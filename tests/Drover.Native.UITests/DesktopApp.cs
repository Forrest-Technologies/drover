namespace Drover.Native.UITests;

/// <summary>
/// The desktop app, started as a process of its own with the settings in its environment. It writes its report to
/// a file and closes when the checks are done.
/// </summary>
internal sealed class DesktopApp(string path, string folder) : IAppUnderCheck
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        var report = Path.Combine(folder, "report.txt");
        var start = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("dotnet") { ArgumentList = { path } }
            : new ProcessStartInfo(path);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.Environment["DROVER_UI_CHECK_REPORT"] = report;

        // A fresh place for the app's settings on Linux, so a server chosen in an earlier run is not remembered.
        foreach (var name in new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME" })
        {
            start.Environment[name] = Path.Combine(folder, "home");
        }

        foreach (var (name, value) in settings)
        {
            start.Environment[name] = value;
        }

        using var app = Process.Start(start) ?? throw new InvalidOperationException($"{path} did not start.");
        app.OutputDataReceived += (_, line) => Echo(logger, line.Data);
        app.ErrorDataReceived += (_, line) => Echo(logger, line.Data);
        app.BeginOutputReadLine();
        app.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(patience);
        try
        {
            await app.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("The app did not finish within {Minutes} minutes and was stopped", patience.TotalMinutes);
            app.Kill(entireProcessTree: true);
        }

        return File.Exists(report) ? await File.ReadAllLinesAsync(report) : [];
    }

    #endregion

    #region Private Methods

    private static void Echo(ILogger logger, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            logger.LogInformation("app: {Line}", line);
        }
    }

    #endregion
}
