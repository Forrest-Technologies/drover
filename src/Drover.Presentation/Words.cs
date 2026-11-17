namespace Drover.Ui;

/// <summary>Small pieces of wording every screen shares, so counts, lists, and sizes read the same.</summary>
public static class Words
{
    #region Private Fields

    private static readonly string[] sizeUnits = ["bytes", "KB", "MB", "GB"];

    #endregion

    #region Public Methods

    /// <summary>"1 routine", "3 routines". The plural is the singular with an s unless one is given.</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        count is 1 ? $"1 {singular}" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} {plural ?? $"{singular}s"}";

    /// <summary>"Monday", "Monday and Friday", "Monday, Wednesday and Friday".</summary>
    public static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    /// <summary>"1st", "2nd", "3rd", "11th", "22nd".</summary>
    public static string Ordinal(int number) => (number % 100, number % 10) switch
    {
        ( >= 11 and <= 13, _) => $"{number}th",
        (_, 1) => $"{number}st",
        (_, 2) => $"{number}nd",
        (_, 3) => $"{number}rd",
        _ => $"{number}th",
    };

    /// <summary>A file size a person can compare: "840 bytes", "12 KB", "3.4 MB".</summary>
    public static string Size(long bytes)
    {
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < sizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit is 0 || value >= 10
            ? $"{Math.Round(value).ToString("N0", CultureInfo.InvariantCulture)} {sizeUnits[unit]}"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {sizeUnits[unit]}";
    }

    /// <summary>How long something took: "under a second", "12 seconds", "3 minutes", "2 hours 5 minutes".</summary>
    public static string Duration(TimeSpan duration) => duration switch
    {
        { TotalSeconds: < 1 } => "under a second",
        { TotalSeconds: < 90 } => Count((int)Math.Round(duration.TotalSeconds), "second"),
        { TotalMinutes: < 90 } => Count((int)Math.Round(duration.TotalMinutes), "minute"),
        _ when duration.Minutes is 0 => Count((int)duration.TotalHours, "hour"),
        _ => $"{Count((int)duration.TotalHours, "hour")} {Count(duration.Minutes, "minute")}",
    };

    /// <summary>A clock for something in progress: "0:07", "12:40".</summary>
    public static string Clock(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture)}";

    #endregion
}
