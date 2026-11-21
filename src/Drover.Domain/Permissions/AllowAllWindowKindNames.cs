namespace Drover.Domain.Permissions;

/// <summary>The spellings of <see cref="AllowAllWindowKind"/> in the data model and the audit log.</summary>
public static class AllowAllWindowKindNames
{
    #region Public Methods

    /// <summary>The stored name, including the number in the two timed kinds.</summary>
    public static string Of(AllowAllWindowKind kind) =>
        kind switch
        {
            AllowAllWindowKind.Minutes30 => "minutes_30",
            AllowAllWindowKind.Hour1 => "hour_1",
            AllowAllWindowKind.UntilDone => "until_done",
            AllowAllWindowKind.UntilOff => "until_off",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown allow-all window kind."),
        };

    /// <summary>The kind a stored name stands for.</summary>
    public static AllowAllWindowKind Read(string text) =>
        text switch
        {
            "minutes_30" => AllowAllWindowKind.Minutes30,
            "hour_1" => AllowAllWindowKind.Hour1,
            "until_done" => AllowAllWindowKind.UntilDone,
            "until_off" => AllowAllWindowKind.UntilOff,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Unknown allow-all window kind."),
        };

    #endregion
}
