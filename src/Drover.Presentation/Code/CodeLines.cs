namespace Drover.Presentation.Code;

/// <summary>
/// Reading a block line by line: whether its lines are a diff or a log, what each diff line is, where a log line's
/// time and level word stand, and the cap on how much of a long block is laid out at once.
/// </summary>
public static partial class CodeLines
{
    #region Private Fields

    /// <summary>The most lines laid out before "Show all" is asked for.</summary>
    public const int MaxLines = 400;

    /// <summary>The most characters laid out before "Show all" is asked for: 64 KiB.</summary>
    public const int MaxCharacters = 65_536;

    // Of the lines with words, the share that must look like a diff's, or a log's, for the block to be one.
    private const double DiffShare = 0.8;

    private const double LogShare = 0.6;

    private const int MinimumLogLines = 2;

    // A level word stands near a log line's start, after its time where it has one.
    private const int LevelReach = 48;

    #endregion

    #region Public Methods

    /// <summary>The lines of a text, without their line breaks. A last line break ends the last line, and starts no new one.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        List<string> lines = [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];

        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>
    /// True when the text is a diff: a <c>diff --git</c>, <c>@@</c>, or <c>---</c> and <c>+++</c> header, at least one
    /// added or removed line, and most lines in a diff's shape.
    /// </summary>
    public static bool IsDiff(IReadOnlyList<string> lines)
    {
        var filled = lines.Where(line => line.Length > 0).ToList();
        var headed = filled.Any(line => line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("@@", StringComparison.Ordinal))
            || (filled.Any(line => line.StartsWith("--- ", StringComparison.Ordinal)) && filled.Any(line => line.StartsWith("+++ ", StringComparison.Ordinal)));
        var changed = filled.Any(line => DiffKindOf(line) is DiffLineKind.Added or DiffLineKind.Removed);
        return headed && changed && filled.Count(IsDiffShaped) >= filled.Count * DiffShare;
    }

    /// <summary>What one line of a diff is. Every line keeps its own <c>+</c> or <c>-</c>, so color is never the only sign.</summary>
    public static DiffLineKind DiffKindOf(string line) => line switch
    {
        _ when line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) => DiffLineKind.Header,
        _ when line.StartsWith("@@", StringComparison.Ordinal) => DiffLineKind.Hunk,
        ['+', ..] => DiffLineKind.Added,
        ['-', ..] => DiffLineKind.Removed,
        _ when DiffHeader().IsMatch(line) => DiffLineKind.Header,
        _ => DiffLineKind.Context,
    };

    /// <summary>True when most of at least two lines with words start with a time or a level word.</summary>
    public static bool IsLog(IReadOnlyList<string> lines)
    {
        var filled = lines.Where(line => line.Trim().Length > 0).ToList();
        return filled.Count >= MinimumLogLines && filled.Count(line => LogLineOf(line) is { Starts: true }) >= filled.Count * LogShare;
    }

    /// <summary>Where a log line's time and level word stand, and how severe the level is.</summary>
    public static LogLine LogLineOf(string line)
    {
        var time = TimeStamp().Match(line);
        var after = time.Success ? time.Index + time.Length : 0;
        var level = LevelWord().Match(line, after, Math.Min(LevelReach, line.Length - after));
        var startsWithLevel = level.Success && line[after..level.Index].Trim(' ', '[', ']', '-', ':', '|').Length == 0;

        return new(
            time.Success ? new Range(time.Index, after) : null,
            level.Success ? new Range(level.Index, level.Index + level.Length) : null,
            level.Success ? SeverityOf(level.Value) : LogSeverity.None,
            time.Success || startsWithLevel);
    }

    /// <summary>
    /// The text to lay out: all of it, or past <see cref="MaxLines"/> lines or <see cref="MaxCharacters"/> characters
    /// the start of it, with the count of all its lines for the "Show all" button.
    /// </summary>
    public static CappedText Capped(string text)
    {
        var lines = LineCount(text);

        if (lines <= MaxLines && text.Length <= MaxCharacters)
        {
            return new(text, lines, false);
        }

        var end = 0;
        for (var line = 0; line < MaxLines && end < text.Length && end <= MaxCharacters; line++)
        {
            var next = text.IndexOf('\n', end);
            end = next < 0 ? text.Length : next + 1;
        }

        return new(text[..Math.Min(end, MaxCharacters)], lines, true);
    }

    /// <summary>The number of lines in a text. A last line break ends the last line, and starts no new one.</summary>
    public static int LineCount(string text) =>
        text.Length == 0 ? 0 : text.Count(character => character is '\n') + (text[^1] is '\n' ? 0 : 1);

    /// <summary>The words of the button that lays out all of a capped block: "Show all 1,200 lines".</summary>
    public static string ShowAll(int lines) => $"Show all {lines.ToString("N0", CultureInfo.InvariantCulture)} lines";

    #endregion

    #region Private Methods

    private static bool IsDiffShaped(string line) =>
        line is ['+' or '-' or ' ', ..] || line.StartsWith("@@", StringComparison.Ordinal) || line.StartsWith('\\') || DiffHeader().IsMatch(line);

    private static LogSeverity SeverityOf(string word) => word switch
    {
        "ERROR" or "ERR" or "FATAL" or "CRITICAL" or "CRIT" => LogSeverity.Failure,
        "WARN" or "WARNING" => LogSeverity.Attention,
        _ => LogSeverity.Quiet,
    };

    [GeneratedRegex(@"^(diff --git |index |new file mode|deleted file mode|similarity index|rename (from|to) |old mode|new mode|Binary files )")]
    private static partial Regex DiffHeader();

    [GeneratedRegex(@"^\[?(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(:\d{2}([.,]\d+)?)?(Z|[+-]\d{2}:?\d{2})?|\d{2}:\d{2}:\d{2}([.,]\d+)?)\]?")]
    private static partial Regex TimeStamp();

    [GeneratedRegex(@"\b(ERROR|ERR|FATAL|CRITICAL|CRIT|WARNING|WARN|INFO|DEBUG|TRACE)\b")]
    private static partial Regex LevelWord();

    #endregion
}

/// <summary>What one line of a diff is.</summary>
public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Hunk,
    Header,
}

/// <summary>How severe a log line's level word is. Only a failure and an attention level take a status color.</summary>
public enum LogSeverity
{
    None,
    Quiet,
    Attention,
    Failure,
}

/// <summary>
/// A log line read: where its time and its level word stand, the level's severity, and whether the line starts like
/// a log line, with a time or a level word.
/// </summary>
public sealed record LogLine(Range? Time, Range? Level, LogSeverity Severity, bool Starts);

/// <summary>The part of a block to lay out, the number of lines in all of it, and whether the part is less than all.</summary>
public sealed record CappedText(string Shown, int TotalLines, bool IsCut);
