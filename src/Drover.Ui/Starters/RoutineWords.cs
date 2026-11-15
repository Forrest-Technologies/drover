namespace Drover.Ui.Starters;

/// <summary>How a routine repeats, as the editor offers it. Each choice shows only its own fields.</summary>
public enum RoutineRhythm
{
    EveryDay,
    Weekdays,
    ChosenDays,
    DayOfMonth,
    EveryHours,
    Custom,
}

/// <summary>How a routine's last run ended.</summary>
public enum RoutineOutcome
{
    Finished,
    Failed,
    DidNotRun,
}

/// <summary>
/// When a routine runs, as the editor's fields hold it. <see cref="Days"/> is read for chosen days,
/// <see cref="DayOfMonth"/> for a day of the month, <see cref="Hours"/> for every N hours, and <see cref="Cron"/>
/// for a custom expression. <see cref="Time"/> is the time of day, or the minute past the hour for every N hours.
/// </summary>
public sealed record RoutineWhen(
    RoutineRhythm Rhythm,
    TimeOnly Time,
    ImmutableSortedSet<DayOfWeek> Days,
    int DayOfMonth,
    int Hours,
    string Cron)
{
    /// <summary>What a new routine starts as: every weekday at nine.</summary>
    public static RoutineWhen Start { get; } =
        new(RoutineRhythm.Weekdays, new TimeOnly(9, 0), [DayOfWeek.Monday], 1, 4, string.Empty);
}

/// <summary>A routine's last run: when, how it ended, why when it did not finish, and where to read it.</summary>
public sealed record RoutineLastRun(DateTimeOffset When, RoutineOutcome Outcome, string? Reason, string? ConversationHref);

/// <summary>One routine as the Routines section lists it.</summary>
public sealed record RoutineItem(
    Guid Id,
    string Prompt,
    string Cron,
    string TimeZone,
    bool Enabled,
    DateTimeOffset? NextRun,
    RoutineLastRun? LastRun);

/// <summary>
/// Routines in words. The editor builds a cron expression from a rhythm and reads any expression it could have
/// built back as a sentence. An expression it could not have built has no sentence here: the server says whether
/// it is valid, and the screen shows it as the expression it is.
/// </summary>
public static class RoutineWords
{
    #region Private Fields

    private static readonly DayOfWeek[] week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
        DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    // Cron's */N restarts at midnight, so only a step that divides 24 repeats evenly all day.
    private static readonly int[] evenHours = [1, 2, 3, 4, 6, 8, 12];

    #endregion

    #region Properties

    /// <summary>The steps "every N hours" offers: the ones that divide the day, so the gaps stay equal.</summary>
    public static IReadOnlyList<int> EvenHours => evenHours;

    /// <summary>The last day of the month a routine may name: every month has it.</summary>
    public const int LastDayEveryMonthHas = 28;

    /// <summary>The days of the week in the order the editor lists them, Monday first.</summary>
    public static IReadOnlyList<DayOfWeek> Week => week;

    #endregion

    #region Public Methods

    /// <summary>The cron expression for what the fields hold, or null when they do not make one.</summary>
    public static string? Cron(RoutineWhen when) => when.Rhythm switch
    {
        RoutineRhythm.EveryDay => $"{when.Time.Minute} {when.Time.Hour} * * *",
        RoutineRhythm.Weekdays => $"{when.Time.Minute} {when.Time.Hour} * * 1-5",
        RoutineRhythm.ChosenDays when when.Days.Count > 0 =>
            $"{when.Time.Minute} {when.Time.Hour} * * {string.Join(',', when.Days.Select(day => (int)day).Order())}",
        RoutineRhythm.DayOfMonth when when.DayOfMonth is >= 1 and <= LastDayEveryMonthHas =>
            $"{when.Time.Minute} {when.Time.Hour} {when.DayOfMonth} * *",
        RoutineRhythm.EveryHours when evenHours.Contains(when.Hours) => $"{when.Time.Minute} */{when.Hours} * * *",
        RoutineRhythm.Custom when !string.IsNullOrWhiteSpace(when.Cron) => when.Cron.Trim(),
        _ => null,
    };

    /// <summary>The fields for a cron expression. One the editor could not have built reads as custom.</summary>
    public static RoutineWhen Read(string cron)
    {
        var custom = RoutineWhen.Start with { Rhythm = RoutineRhythm.Custom, Cron = cron.Trim() };
        if (cron.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [var minuteText, var hourText, var dayText, "*", var weekText]
            || !Number(minuteText, 0, 59, out var minute))
        {
            return custom;
        }

        if (hourText.StartsWith("*/", StringComparison.Ordinal))
        {
            return (dayText, weekText) is ("*", "*") && Number(hourText[2..], 1, 23, out var hours) && evenHours.Contains(hours)
                ? custom with { Rhythm = RoutineRhythm.EveryHours, Time = new TimeOnly(0, minute), Hours = hours }
                : custom;
        }

        if (!Number(hourText, 0, 23, out var hour))
        {
            return custom;
        }

        var timed = custom with { Time = new TimeOnly(hour, minute) };
        return (dayText, weekText) switch
        {
            ("*", "*") => timed with { Rhythm = RoutineRhythm.EveryDay },
            ("*", "1-5") => timed with { Rhythm = RoutineRhythm.Weekdays },
            ("*", _) when ReadDays(weekText) is { } days => timed with { Rhythm = RoutineRhythm.ChosenDays, Days = days },
            (_, "*") when Number(dayText, 1, LastDayEveryMonthHas, out var dayOfMonth) =>
                timed with { Rhythm = RoutineRhythm.DayOfMonth, DayOfMonth = dayOfMonth },
            _ => custom,
        };
    }

    /// <summary>
    /// When a routine runs, as a sentence without its time zone: "Every weekday at 08:00". Null for an expression
    /// the editor could not have built.
    /// </summary>
    public static string? Describe(string cron)
    {
        var when = Read(cron);
        var time = when.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        return when.Rhythm switch
        {
            RoutineRhythm.EveryDay => $"Every day at {time}",
            RoutineRhythm.Weekdays => $"Every weekday at {time}",
            RoutineRhythm.ChosenDays => $"Every {Words.List(DayNames(when.Days))} at {time}",
            RoutineRhythm.DayOfMonth => $"On the {Words.Ordinal(when.DayOfMonth)} of every month at {time}",
            RoutineRhythm.EveryHours when when.Hours is 1 => $"Every hour{Past(when.Time.Minute)}",
            RoutineRhythm.EveryHours => $"Every {when.Hours} hours{Past(when.Time.Minute)}",
            _ => null,
        };
    }

    /// <summary>
    /// The whole sentence a routine is listed and read out as: "Every weekday at 08:00, Berlin time". A custom
    /// expression reads "On a custom schedule, Berlin time", and the screen shows the expression beside it.
    /// </summary>
    public static string Sentence(string cron, string timeZone) =>
        $"{Describe(cron) ?? "On a custom schedule"}, {Zone(timeZone)}";

    /// <summary>A time zone as a person says it: "Europe/Berlin" is "Berlin time".</summary>
    public static string Zone(string timeZone) => timeZone switch
    {
        "" or "UTC" or "Etc/UTC" or "Z" => "UTC",
        _ => $"{timeZone[(timeZone.LastIndexOf('/') + 1)..].Replace('_', ' ')} time",
    };

    /// <summary>How the last run went, given the moment already in words: "Ran today at 08:00 and finished".</summary>
    public static string Outcome(RoutineLastRun? last, string moment) => last switch
    {
        null => "Has not run yet",
        { Outcome: RoutineOutcome.Finished } => $"Ran {moment} and finished",
        { Outcome: RoutineOutcome.Failed, Reason: { Length: > 0 } reason } => $"Failed {moment}: {reason}",
        { Outcome: RoutineOutcome.Failed } => $"Failed {moment}",
        { Reason: { Length: > 0 } reason } => $"Did not run {moment}: {reason}",
        _ => $"Did not run {moment}",
    };

    /// <summary>The closed section's one line: "No routines", "1 routine", "3 routines, 1 off".</summary>
    public static string Summary(IReadOnlyCollection<RoutineItem> routines)
    {
        var off = routines.Count(routine => !routine.Enabled);
        return (routines.Count, off) switch
        {
            (0, _) => "No routines",
            (var count, 0) => Words.Count(count, "routine"),
            (var count, _) => $"{Words.Count(count, "routine")}, {off} off",
        };
    }

    /// <summary>The first line of a prompt, for a row that has room for one.</summary>
    public static string FirstLine(string prompt) =>
        prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// What a bot's proposed routine would do, as the named facts an approval card shows. The prompt is given
    /// whole: it may repeat text the bot read somewhere, and a person approves every word of it.
    /// </summary>
    public static IReadOnlyList<ToolFact> Proposal(string cron, string timeZone, string prompt, RoutineItem? before = null)
    {
        List<ToolFact> facts = [];
        if (before is not null)
        {
            facts.Add(new("When, now", Sentence(before.Cron, before.TimeZone)));
            facts.Add(new("What it is sent, now", before.Prompt));
        }

        facts.Add(new(before is null ? "When" : "When, after the change", Sentence(cron, timeZone)));
        facts.Add(new(before is null ? "What it is sent" : "What it is sent, after the change", prompt));
        return facts;
    }

    #endregion

    #region Private Methods

    private static string[] DayNames(ImmutableSortedSet<DayOfWeek> days) =>
        [.. week.Where(days.Contains).Select(day => day.ToString())];

    private static string Past(int minute) => minute is 0 ? ", on the hour" : $", at {minute} past";

    private static bool Number(string text, int least, int most, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= least && value <= most;

    private static ImmutableSortedSet<DayOfWeek>? ReadDays(string text)
    {
        var days = ImmutableSortedSet.CreateBuilder<DayOfWeek>();
        foreach (var part in text.Split(','))
        {
            // Cron counts Sunday as 0 and also as 7.
            if (!Number(part, 0, 7, out var day))
            {
                return null;
            }

            days.Add((DayOfWeek)(day % 7));
        }

        return days.ToImmutable();
    }

    #endregion
}
