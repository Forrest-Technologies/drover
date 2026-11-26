namespace Drover.Contracts.Themes;

/// <summary>
/// A theme in one mode, resolved into the values a screen draws with. Colors and numbers are named as the token file
/// names them, without the "--dv-" prefix: "bg-0", "space-4", "dur-2". A client turns the names into its own, and
/// everything that is derived (the scrim, the yard color, the spacing steps, the durations) is already worked out.
/// </summary>
public sealed record ThemeTokenSet(
    ThemeFile Theme,
    ColorMode Mode,
    IReadOnlyDictionary<string, Argb> Colors,
    IReadOnlyDictionary<string, double> Numbers,
    ThemeFaces Faces)
{
    /// <summary>The id of the theme these values came from.</summary>
    public string ThemeId => Theme.Id;
}

/// <summary>
/// The faces a theme draws with, each as it is drawn now: the face a file names that is not bundled yet is
/// replaced by the bundled face of its kind.
/// </summary>
public sealed record ThemeFaces(
    FontFaces.Face Interface,
    FontFaces.Face Machine,
    FontFaces.Face Reading,
    FontFaces.Face Heading,
    FontFaces.Face Numerals);
