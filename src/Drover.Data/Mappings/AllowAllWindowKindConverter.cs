namespace Drover.Data.Mappings;

/// <summary>
/// The two timed kinds are spelled with a number in the data model (<c>minutes_30</c>, <c>hour_1</c>), which the
/// general snake-case converter cannot produce from a PascalCase member name. This converter names those values
/// exactly and uses the general spelling for the other two.
/// </summary>
public sealed class AllowAllWindowKindConverter()
    : ValueConverter<AllowAllWindowKind, string>(
        kind => Spell(kind),
        text => Read(text))
{
    #region Private Methods

    private static string Spell(AllowAllWindowKind kind) =>
        kind switch
        {
            AllowAllWindowKind.Minutes30 => "minutes_30",
            AllowAllWindowKind.Hour1 => "hour_1",
            AllowAllWindowKind.UntilDone => "until_done",
            AllowAllWindowKind.UntilOff => "until_off",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown allow-all window kind."),
        };

    private static AllowAllWindowKind Read(string text) =>
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
