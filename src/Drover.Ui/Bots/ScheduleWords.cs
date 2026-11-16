namespace Drover.Ui.Bots;

/// <summary>Moments in words. Routines are in <see cref="RoutineWords"/>.</summary>
public static class ScheduleWords
{
    #region Public Methods

    /// <summary>A moment in the reader's own time, short enough for one line.</summary>
    public static string Moment(DateTimeOffset moment) =>
        moment.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture);

    #endregion
}
