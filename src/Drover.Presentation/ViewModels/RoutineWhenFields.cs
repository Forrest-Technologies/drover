namespace Drover.Presentation.ViewModels;

/// <summary>
/// The "When" part of writing a routine: one choice of rhythm, then only that rhythm's fields, and under them the
/// sentence the routine will be listed as. Nobody has to know cron to use it. Each change hands back a new value.
/// </summary>
public static class RoutineWhenFields
{
    #region Private Fields

    private static readonly ChoiceOption[] rhythms =
    [
        new(nameof(RoutineRhythm.EveryDay), "Every day"),
        new(nameof(RoutineRhythm.Weekdays), "Every weekday"),
        new(nameof(RoutineRhythm.ChosenDays), "On days I choose"),
        new(nameof(RoutineRhythm.DayOfMonth), "Once a month"),
        new(nameof(RoutineRhythm.EveryHours), "Every few hours"),
        new(nameof(RoutineRhythm.Custom), "Custom"),
    ];

    #endregion

    #region Properties

    public static string WhenLabel => "When";

    public static IReadOnlyList<ChoiceOption> Rhythms => rhythms;

    public static string DaysLegend => "Days";

    public static string DayOfMonthLabel => "Day of the month";

    public static string DayOfMonthHint => $"1 to {RoutineWords.LastDayEveryMonthHas}, so it runs every month. For the end of the month, use Custom.";

    public static string HoursLabel => "Every how many hours";

    public static IReadOnlyList<ChoiceOption> HoursOptions =>
        [.. RoutineWords.EvenHours.Select(hours => new ChoiceOption(Number(hours), Words.Count(hours, "hour")))];

    public static string MinuteLabel => "Minutes past the hour";

    public static string CronLabel => "Cron expression";

    public static string CronHint => "Five fields: minute, hour, day of month, month, day of week.";

    public static string AtLabel => "At";

    public static string TimeZoneLabel => "Time zone";

    public static string TimeZoneHint => "A zone name, such as Europe/Berlin.";

    #endregion

    #region Public Methods

    /// <summary>The time of day is asked for every rhythm but every few hours and a custom expression.</summary>
    public static bool AtShown(RoutineWhen when) => when.Rhythm is not (RoutineRhythm.EveryHours or RoutineRhythm.Custom);

    public static string At(RoutineWhen when) => when.Time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>What the fields come to, in words, always shown so a change to it is announced.</summary>
    public static string Reads(RoutineWhen when, string timeZone, string? serverProblem, IReadOnlyList<DateTimeOffset>? next) =>
        (RoutineWords.Cron(when), serverProblem) switch
        {
            (null, _) when when.Rhythm is RoutineRhythm.ChosenDays => "Choose at least one day.",
            (null, _) when when.Rhythm is RoutineRhythm.Custom => "Write the expression. Drover checks it as you type.",
            (null, _) => "That number is out of range.",
            ({ } cron, { Length: > 0 }) when RoutineWords.Describe(cron) is null => $"The expression {cron} cannot be saved as it is.",
            ({ } cron, _) when RoutineWords.Describe(cron) is null && next is { Count: > 0 } times =>
                $"Runs on the expression {cron}, {RoutineWords.Zone(timeZone)}. Next: {string.Join("; ", times.Select(time => RoutineWords.Moment(time, timeZone)))}.",
            ({ } cron, _) when RoutineWords.Describe(cron) is null => $"Runs on the expression {cron}, {RoutineWords.Zone(timeZone)}. Checking it.",
            ({ } cron, _) => $"Reads as: {RoutineWords.Sentence(cron, timeZone)}.",
        };

    public static RoutineWhen ChooseRhythm(RoutineWhen when, string value) => when with { Rhythm = Enum.Parse<RoutineRhythm>(value) };

    public static RoutineWhen ToggleDay(RoutineWhen when, DayOfWeek day, bool chosen) =>
        when with { Days = chosen ? when.Days.Add(day) : when.Days.Remove(day) };

    public static RoutineWhen SetDayOfMonth(RoutineWhen when, string text) => when with { DayOfMonth = Whole(text, 0) };

    public static RoutineWhen SetHours(RoutineWhen when, string text) => when with { Hours = Whole(text, 0) };

    public static RoutineWhen SetMinute(RoutineWhen when, string text) => when with { Time = new TimeOnly(0, Math.Clamp(Whole(text, 0), 0, 59)) };

    public static RoutineWhen SetCron(RoutineWhen when, string text) => when with { Cron = text };

    /// <summary>A time field gives hours and minutes. Text that is not a time yet changes nothing, or null.</summary>
    public static RoutineWhen? SetTime(RoutineWhen when, string text) =>
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var time) ? when with { Time = new TimeOnly(time.Hour, time.Minute) } : null;

    #endregion

    #region Private Methods

    private static int Whole(string text, int fallback) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    #endregion
}
