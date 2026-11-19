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
                    !line.StartsWith(Program.ResultPrefix, StringComparison.Ordinal));
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
        var start = new ProcessStartInfo("xcrun")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("simctl");
        start.ArgumentList.Add("launch");
        start.ArgumentList.Add("--console");
        start.ArgumentList.Add("--terminate-running-process");
        start.ArgumentList.Add(device);
        start.ArgumentList.Add(Bundle);

        foreach (var (name, value) in settings)
        {
            start.Environment[$"SIMCTL_CHILD_{name}"] = value;
        }

        using var app = Process.Start(start) ?? throw new InvalidOperationException("The iOS app did not start.");
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(patience);
        var output = ReadReport(app.StandardOutput, result, logger, timeout.Token);
        var error = ReadReport(app.StandardError, result, logger, timeout.Token);
        var drained = Task.WhenAll(output, error);
        var outcome = "ui-check result: failed";

        try
        {
            await Task.WhenAny(result.Task, drained).WaitAsync(timeout.Token);
            if (result.Task.IsCompletedSuccessfully)
            {
                outcome = await result.Task;
            }
            else
            {
                logger.LogError("The iOS app console closed before it reported a result");
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.LogError("The iOS app did not report a result within {Minutes} minutes", patience.TotalMinutes);
        }
        finally
        {
            await Terminate();
            await drained;
        }

        return [.. (await output).Concat(await error), outcome];
    }

    private static async Task<IReadOnlyList<string>> ReadReport(
        StreamReader reader,
        TaskCompletionSource<string> result,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        List<string> lines = [];
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var at = line.IndexOf("ui-check", StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }

                var report = line[at..].TrimEnd();
                if (report is "ui-check result: passed" or "ui-check result: failed")
                {
                    result.TrySetResult(report);
                }
                else
                {
                    lines.Add(report);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("The iOS app console stopped after the report deadline");
        }

        return lines;
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
