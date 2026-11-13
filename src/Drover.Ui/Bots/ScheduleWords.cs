namespace Drover.Ui.Bots;

/// <summary>How often a schedule made in the editor repeats.</summary>
public enum ScheduleRhythm
{
    Daily,
    Weekdays,
    Weekly,
    Custom,
}

/// <summary>
/// Schedules in words. The editor builds the common ones from a rhythm and a time, and reads them back as a
/// sentence; anything else is shown as the cron expression it is.
/// </summary>
public static class ScheduleWords
{
    #region Private Fields

    private static readonly string[] days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    #endregion

    #region Properties

    public static IReadOnlyList<string> Days => days;

    #endregion

    #region Public Methods

    /// <summary>The cron expression for a rhythm at a time of day, or null when the time does not read.</summary>
    public static string? Cron(ScheduleRhythm rhythm, string time, int day)
    {
        // A time field gives hours and minutes, and seconds too in some browsers.
        if (time.Split(':') is not [var hourText, var minuteText, ..]
            || !int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) || hour > 23
            || !int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out var minute) || minute > 59)
        {
            return null;
        }

        return rhythm switch
        {
            ScheduleRhythm.Daily => $"{minute} {hour} * * *",
            ScheduleRhythm.Weekdays => $"{minute} {hour} * * 1-5",
            ScheduleRhythm.Weekly => $"{minute} {hour} * * {day}",
            _ => null,
        };
    }

    /// <summary>The rhythm, time, and day a cron expression stands for, when it is one the editor builds.</summary>
    public static (ScheduleRhythm Rhythm, string Time, int Day) Read(string cron)
    {
        var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields is [var minute, var hour, "*", "*", var weekday]
            && int.TryParse(minute, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes < 60
            && int.TryParse(hour, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) && hours < 24)
        {
            var time = $"{hours:00}:{minutes:00}";

            if (weekday is "*")
            {
                return (ScheduleRhythm.Daily, time, 1);
            }

            if (weekday is "1-5")
            {
                return (ScheduleRhythm.Weekdays, time, 1);
            }

            if (int.TryParse(weekday, NumberStyles.None, CultureInfo.InvariantCulture, out var day) && day < days.Length)
            {
                return (ScheduleRhythm.Weekly, time, day);
            }
        }

        return (ScheduleRhythm.Custom, "09:00", 1);
    }

    /// <summary>A schedule as a sentence, such as "Every weekday at 09:30".</summary>
    public static string Describe(string cron)
    {
        var (rhythm, time, day) = Read(cron);
        return rhythm switch
        {
            ScheduleRhythm.Daily => $"Every day at {time}",
            ScheduleRhythm.Weekdays => $"Every weekday at {time}",
            ScheduleRhythm.Weekly => $"Every {days[day]} at {time}",
            _ => cron,
        };
    }

    /// <summary>A moment in the reader's own time, short enough for one line.</summary>
    public static string Moment(DateTimeOffset moment) =>
        moment.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture);

    #endregion
}
