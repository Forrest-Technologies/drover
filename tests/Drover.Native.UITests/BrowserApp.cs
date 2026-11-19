namespace Drover.Native.UITests;

/// <summary>
/// The browser build, served by the test server under <c>/native/</c> and opened by the screen-check script. The
/// settings go into the page's local storage before the app starts, and the report comes back on the page console.
/// </summary>
internal sealed class BrowserApp(string webRoot) : IAppUnderCheck
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromMinutes(5);

    #endregion

    #region Properties

    /// <summary>The published <c>wwwroot</c> the test server serves.</summary>
    public string WebRoot { get; } = Path.GetFullPath(webRoot);

    #endregion

    #region Interface Implementations

    public async Task<IReadOnlyList<string>> Run(IReadOnlyDictionary<string, string> settings, ILogger logger)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Script());

        foreach (var (name, value) in settings)
        {
            start.Environment[name] = value;
        }

        using var node = Process.Start(start) ?? throw new InvalidOperationException("node did not start.");
        var output = node.StandardOutput.ReadToEndAsync();
        var error = node.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(patience);
        try
        {
            await node.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException exception)
        {
            logger.LogError(
                exception,
                "The browser checks did not finish within {Minutes} minutes and were stopped",
                patience.TotalMinutes);
            node.Kill(entireProcessTree: true);
        }

        var lines = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            logger.LogInformation("browser: {Line}", line);
        }

        var errors = await error;
        if (node.ExitCode != 0 && errors.Length > 0)
        {
            logger.LogError("The browser checks ended with {Code}: {Error}", node.ExitCode, errors);
        }

        return lines;
    }

    #endregion

    #region Private Methods

    private static string Script()
    {
        string[] roots = [Directory.GetCurrentDirectory(), AppContext.BaseDirectory];
        foreach (var start in roots)
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "scripts", "check-native-screens.mjs");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("The browser screen-check script is not in this tree.");
    }

    #endregion
}
