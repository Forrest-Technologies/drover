namespace Drover.Native.Checks;

/// <summary>
/// A theme made only for the screen checks, a sample and not a look: Night Yard with a different value in every
/// group that changes at run time, so a check can tell that each one reached the open screen. It is never offered to
/// a person.
/// </summary>
internal static class SampleTheme
{
    #region Properties

    public const string Id = "sample-for-checks";

    public const string DisplayName = "Sample theme for checks";

    /// <summary>The base size of the type scale, which Night Yard does not have at any step.</summary>
    public const int BaseSize = 15;

    public const int ControlCut = 9;

    public const int FrameCut = 14;

    public const int RowTouch = 56;

    public const int RowPointer = 52;

    public const int SpaceFour = 19;

    public const int Bloom = 4;

    public const int Duration = 286;

    public const int YardStep = 24;

    public const double CharacterLine = 3;

    public static ThemeFile Value { get; } = Make();

    #endregion

    #region Private Methods

    private static ThemeFile Make()
    {
        var night = BuiltInThemes.NightYard;
        var colors = night.Dark.Colors.ToDictionary(pair => pair.Key, pair => pair.Value);
        colors["ember"] = Rgb.Parse("#33AA55");

        return night with
        {
            Id = Id,
            Name = DisplayName,
            Intent = "A sample made for the screen checks.",
            Type = night.Type with { Scale = [12, 13, BaseSize, 17, 21, 29] },
            Density = night.Density with { Spacing = 1.2, Row = new(RowPointer, RowTouch), ComposerLines = 6 },
            Shape = night.Shape with
            {
                Corners = CornerLanguage.Ticked,
                Cut = new(ControlCut, FrameCut),
                Registration = true,
                ControlRadius = 0,
                CardRadius = 0,
                SheetRadius = 0,
                Line = 2,
            },
            Surfaces = night.Surfaces with { Texture = YardTexture.Grid, YardStrength = 0.4, YardStep = YardStep },
            Motion = night.Motion with { Scale = 1.3, Bloom = Bloom, Breath = 3380, Travel = 1690, Live = LiveStyle.Fade, TravelLine = 3 },
            Characters = night.Characters with { Facet = 0.12, Line = CharacterLine, Visor = VisorStyle.Outlined },
            Dark = night.Dark with { Colors = colors },
        };
    }

    #endregion
}
