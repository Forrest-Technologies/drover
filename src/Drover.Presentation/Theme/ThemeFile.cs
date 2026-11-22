namespace Drover.Ui.Theme;

/// <summary>The corner language the shape class draws.</summary>
public enum CornerLanguage
{
    Faceted,
    Square,
    Ticked,
}

/// <summary>The texture of the working area.</summary>
public enum YardTexture
{
    Dots,
    Lines,
    Grid,
    None,
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
public sealed record ThemeSurfaces(int Levels, YardTexture Texture, double YardStrength, int YardStep);

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
/// both modes, and every token group. Read one with <see cref="ThemeFileReader"/>; resolve one into the values a
/// screen draws with through <see cref="ThemeResolution.Resolve"/>.
/// </summary>
public sealed record ThemeFile(
    string Id,
    string Name,
    string Intent,
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

    /// <summary>The colors of one mode.</summary>
    public ModeColors In(ColorMode mode) => mode is ColorMode.Dark ? Dark : Light;
}
