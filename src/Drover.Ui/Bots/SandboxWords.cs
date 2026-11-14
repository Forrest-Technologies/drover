namespace Drover.Ui.Bots;

/// <summary>
/// A bot's sandbox in words: its state, when it was last used, and how long the sandbox screen waits between two
/// pictures. The state is always said in words, so a color is never the only signal.
/// </summary>
public static class SandboxWords
{
    #region Private Fields

    private static readonly TimeSpan refresh = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan longestWait = TimeSpan.FromSeconds(30);

    #endregion

    #region Public Methods

    public static string State(string state) => state switch
    {
        "starting" => "Starting",
        "running" => "Running",
        "paused" => "Paused",
        "replacing" => "Being replaced",
        _ => "No sandbox yet",
    };

    /// <summary>What the badge adds to the words: running is fine, a change under way asks for attention.</summary>
    public static BadgeKind Badge(string state) => state switch
    {
        "running" => BadgeKind.Ok,
        "starting" or "replacing" => BadgeKind.Warn,
        _ => BadgeKind.Neutral,
    };

    public static string LastUsed(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } used)
        {
            return "Not used yet";
        }

        var ago = now - used;
        return ago switch
        {
            { TotalMinutes: < 1 } => "Used just now",
            { TotalMinutes: < 2 } => "Used a minute ago",
            { TotalHours: < 1 } => $"Used {(int)ago.TotalMinutes} minutes ago",
            { TotalHours: < 2 } => "Used an hour ago",
            { TotalDays: < 1 } => $"Used {(int)ago.TotalHours} hours ago",
            _ => $"Used {used.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)}",
        };
    }

    /// <summary>The line the bot editor's Sandbox section shows: the state and the last use.</summary>
    public static string Summary(BotSandboxStatus status, DateTimeOffset now) =>
        status.State is "none" ? State(status.State) : $"{State(status.State)} · {LastUsed(status.LastUsedAt, now).ToLowerInvariant()}";

    /// <summary>How a command ended, in one line: its exit code, or that it was stopped, and what else is worth saying.</summary>
    public static string Outcome(SandboxCommandResponse result)
    {
        List<string> parts =
        [
            result.TimedOut || result.ExitStatus is null ? "Stopped: it ran past the time limit." : $"Exit code {result.ExitStatus}.",
        ];

        if (result.Truncated)
        {
            parts.Add("Some output was left out.");
        }

        if (result.NewSandbox)
        {
            parts.Add("This is a new sandbox; the old one and its files are gone.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// How long to wait before the next picture: two seconds, doubled after each failure in a row, at most thirty,
    /// so a sandbox that does not answer is not asked again and again.
    /// </summary>
    public static TimeSpan NextPictureAfter(int failuresInARow)
    {
        var wait = refresh * Math.Pow(2, Math.Clamp(failuresInARow, 0, 8));
        return wait < longestWait ? wait : longestWait;
    }

    #endregion
}
