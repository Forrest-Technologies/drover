namespace Drover.Ui.Theme;

/// <summary>
/// One hue a person may give the color of their own intent. The theme's own color has no hue here: it is the color
/// the theme file holds.
/// </summary>
public sealed record Accent(string Id, string Name, double? Hue);

/// <summary>
/// The accents a person may choose. The color of a person's intent keeps its meaning in every theme; an accent only
/// turns its hue. The color is worked out for each theme and mode, at the theme's own chroma and as close to its
/// lightness as still reads on every surface, and an accent is offered for a theme only when the theme then passes
/// every gate: text contrast, the focus ring, and the distance in hue from the color of a bot working and of work
/// passing between bots.
/// </summary>
public static class Accents
{
    #region Private Fields

    /// <summary>The id of the theme's own color, which a device that never chose an accent has.</summary>
    public const string ThemeId = "theme";

    private const double TextMinimum = 4.5;

    private const double LightnessStep = 0.005;

    private const double LightestTried = 0.97;

    private const double DarkestTried = 0.15;

    // An accent this close in hue to the theme's own color is the same choice under another name.
    private const double SameHue = 15;

    private const double FullTurn = 360;

    private static readonly Rgb black = new(0, 0, 0);

    private static readonly Rgb white = new(byte.MaxValue, byte.MaxValue, byte.MaxValue);

    private static readonly string[] surfaces = ["bg-0", "bg-1", "bg-2", "bg-3"];

    private static readonly Accent[] all =
    [
        new(ThemeId, "Theme color", null),
        new("orange", "Orange", 55),
        new("green", "Green", 150),
        new("blue", "Blue", 255),
    ];

    private static readonly ConcurrentDictionary<(ThemeFile Theme, string Accent), ThemeFile> turned = new();

    private static readonly ConcurrentDictionary<ThemeFile, IReadOnlyList<Accent>> offered = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// The accents this theme offers, its own color first: each one leaves the theme passing every gate, and none
    /// repeats the theme's own color.
    /// </summary>
    public static IReadOnlyList<Accent> For(ThemeFile theme) => offered.GetOrAdd(theme, Offered);

    /// <summary>
    /// The theme with the color of a person's intent turned to this accent, in its dark and its light mode. The
    /// theme itself when it does not offer the accent, as for a device that never chose one. The same theme and
    /// accent always give the same object, so a screen can tell whether the look in use changed.
    /// </summary>
    public static ThemeFile WithAccent(this ThemeFile theme, string accentId) =>
        For(theme).FirstOrDefault(accent => accent.Id == accentId) is { Hue: { } hue } accent
            ? turned.GetOrAdd((theme, accent.Id), _ => Turn(theme, hue))
            : theme;

    #endregion

    #region Private Methods

    private static IReadOnlyList<Accent> Offered(ThemeFile theme) =>
    [
        .. all.Where(accent => accent.Hue is not { } hue
            || (HueDistance(hue, theme.Dark.Colors["ember"].Hue()) >= SameHue
                && ThemeGates.Check(Turn(theme, hue)).Count == 0)),
    ];

    private static ThemeFile Turn(ThemeFile theme, double hue) =>
        theme with { Dark = Turn(theme.Dark, hue, lighten: true), Light = Turn(theme.Light, hue, lighten: false) };

    // The accent starts at the lightness of the theme's own color and moves away from the surfaces, lighter in a
    // dark mode and darker in a light one, until it reads as text on every one of them. Text on the accent is the
    // theme's own where that still reads, and otherwise black or white.
    private static ModeColors Turn(ModeColors mode, double hue, bool lighten)
    {
        var (lightness, chroma, _) = mode.Colors["ember"].ToOklch();
        var accent = Rgb.FromOklch(lightness, chroma, hue);

        while (!ReadsOnEverySurface(accent, mode) && lightness is > DarkestTried and < LightestTried)
        {
            lightness += lighten ? LightnessStep : -LightnessStep;
            accent = Rgb.FromOklch(lightness, chroma, hue);
        }

        Rgb[] inks = [mode.Colors["on-accent"], black, white];
        var onAccent = inks.FirstOrDefault(
            ink => WcagContrast.Ratio(ink, accent) >= TextMinimum,
            inks.MaxBy(ink => WcagContrast.Ratio(ink, accent)));
        Dictionary<string, Rgb> colors = new(mode.Colors) { ["ember"] = accent, ["on-accent"] = onAccent };

        return mode with { Colors = colors };
    }

    private static bool ReadsOnEverySurface(Rgb accent, ModeColors mode) =>
        surfaces.All(surface => WcagContrast.Ratio(accent, mode.Colors[surface]) >= TextMinimum);

    private static double HueDistance(double first, double second)
    {
        var apart = Math.Abs(first - second);

        return Math.Min(apart, FullTurn - apart);
    }

    #endregion
}
