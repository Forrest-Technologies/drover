namespace Drover.Presentation.ViewModels;

/// <summary>One row of the report. The word in the badge carries the state; the color only adds to it.</summary>
public sealed record StatusEntry(string Name, string Summary, BadgeKind Kind, string Word, string? Checked)
{
    #region Properties

    public bool HasChecked => Checked is not null;

    #endregion

    #region Public Methods

    public static StatusEntry From(StatusRow row) => new(
        row.Name,
        row.Summary,
        row.State switch
        {
            "ok" => BadgeKind.Ok,
            "warning" => BadgeKind.Warn,
            "problem" => BadgeKind.Danger,
            _ => BadgeKind.Neutral,
        },
        row.State switch
        {
            "ok" => "Working",
            "warning" => "Needs attention",
            "problem" => "Not working",
            _ => "Off",
        },
        row.CheckedAt is { } at ? $"checked {at.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)}" : null);

    #endregion
}
