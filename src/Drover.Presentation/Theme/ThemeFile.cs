namespace Drover.Ui.Theme;

/// <summary>The corner language the shape class draws.</summary>
public enum CornerLanguage
{
    Faceted,
    Square,
    Ticked,

    /// <summary>Every corner rounded by a large radius, so a control at its smallest height is a pill.</summary>
    Rounded,
}

/// <summary>Where the Appearance page lists a theme. It groups and changes no token and no bound.</summary>
public enum ThemeGroup
{
    /// <summary>A look with a character of its own.</summary>
    Expressive,

    /// <summary>A restrained look for work settings.</summary>
    Professional,
}

/// <summary>The texture of the working area.</summary>
public enum YardTexture
{
    Dots,
    Lines,
    Grid,
    None,
}

/// <summary>How a conversation is laid out.</summary>
public enum ConversationStyle
{
    /// <summary>A bot's words run the full width with no bubble, beside its head. A person's sit in a bubble.</summary>
    Document,

    /// <summary>
    /// Both sides speak in bubbles: a person's in a tint of the color of their intent, a bot's on a card surface
    /// under its head and name.
    /// </summary>
    Bubbles,
}

/// <summary>How a live state shows: a loop for as long as it lasts, or one fade to a still state.</summary>
public enum LiveStyle
{
    Loop,
    Fade,
}

/// <summary>
/// How a character's visor is drawn: a band, or a band with its edge drawn so it reads without bloom.
/// </summary>
public enum VisorStyle
{
    Band,
    Outlined,
}

/// <summary>Which face sets headings.</summary>
public enum HeadingFace
{
    Interface,
    Machine,
}

/// <summary>A request a device can make, which a theme is suggested for.</summary>
public enum DeviceRequest
{
    LargeText,
    ReducedMotion,
}

/// <summary>A pair of values for the two kinds of input: a pointer, and a finger.</summary>
public sealed record InputPair(int ForPointer, int ForTouch);

/// <summary>A pair of values for a control and for a larger frame.</summary>
public sealed record SizePair(int Control, int Frame);

/// <summary>The type group.</summary>
public sealed record ThemeType(
    string Interface,
    string Machine,
    string? Reading,
    HeadingFace Headings,
    bool MachineNumerals,
    IReadOnlyList<int> Scale,
    int BodyWeight,
    int LabelWeight,
    int HeadingWeight,
    double TextLeading,
    double HeadingLeading,
    double Tracking,
    double HeadingTracking,
    int Measure);

/// <summary>The density group.</summary>
public sealed record ThemeDensity(double Spacing, InputPair Control, InputPair Row, int ComposerLines);

/// <summary>The shape group.</summary>
public sealed record ThemeShape(
    CornerLanguage Corners,
    SizePair Cut,
    bool Registration,
    int ControlRadius,
    int CardRadius,
    int SheetRadius,
    int Line,
    double IconStroke);

/// <summary>The surfaces group.</summary>
public sealed record ThemeSurfaces(
    int Levels,
    YardTexture Texture,
    double YardStrength,
    int YardStep,
    ConversationStyle Conversation);

/// <summary>The motion group.</summary>
public sealed record ThemeMotion(double Scale, int Bloom, int Breath, int Travel, LiveStyle Live, int TravelLine);

/// <summary>The characters group.</summary>
public sealed record ThemeCharacters(
    double HeadLightness,
    double HeadChroma,
    double Facet,
    double Line,
    VisorStyle Visor);

/// <summary>
/// One mode of a theme: how its hues were made and its 43 color tokens, by name without the "--dv-" prefix.
/// </summary>
public sealed record ModeColors(double HueLightness, double HueChroma, IReadOnlyDictionary<string, Rgb> Colors);

/// <summary>
/// A theme as the closed theme-file format of <c>docs/architecture/09-design-direction.md</c> defines it: data only,
/// a dark and a light mode, and every token group. The black mode is not stored: it is worked out from the dark one. Read one with <see cref="ThemeFileReader"/>; resolve one into the values a
/// screen draws with through <see cref="ThemeResolution.Resolve"/>.
/// </summary>
public sealed record ThemeFile(
    string Id,
    string Name,
    string Intent,
    ThemeGroup Group,
    ColorMode FirstMode,
    IReadOnlyList<DeviceRequest> Suits,
    ThemeType Type,
    ThemeDensity Density,
    ThemeShape Shape,
    ThemeSurfaces Surfaces,
    ThemeMotion Motion,
    ThemeCharacters Characters,
    ModeColors Dark,
    ModeColors Light)
{
    /// <summary>The format this reader understands.</summary>
    public const int Format = 1;

    private static readonly Rgb black = new(0, 0, 0);

    private static readonly string[] surfaces = ["bg-0", "bg-1", "bg-2", "bg-3"];

    /// <summary>The colors of one mode.</summary>
    public ModeColors In(ColorMode mode) => mode switch
    {
        ColorMode.Light => Light,
        ColorMode.Black => BlackOf(Dark),
        _ => Dark,
    };

    // Black is the dark mode on a screen that turns its pixels off: the first surface is black, and each surface
    // above it takes the color of the one below it in the dark mode. Everything drawn on a surface keeps its color,
    // so every pair that passes in the dark mode stands on a darker surface here.
    private static ModeColors BlackOf(ModeColors dark)
    {
        var colors = dark.Colors.ToDictionary(color => color.Key, color => color.Value);
        if (surfaces.All(dark.Colors.ContainsKey))
        {
            colors[surfaces[0]] = black;
            foreach (var level in (int[])[1, 2, 3])
            {
                colors[surfaces[level]] = dark.Colors[surfaces[level - 1]];
            }
        }

        return dark with { Colors = colors };
    }
}
