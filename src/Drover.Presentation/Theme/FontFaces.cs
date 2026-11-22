namespace Drover.Ui.Theme;

/// <summary>
/// The faces a theme file may name. A face is bundled when its file ships in the app; one that is named but not yet
/// bundled is drawn in the bundled face of the same kind until the issue that ships it does. A theme file never
/// names a font file, a URL or a path: it names one of these ids.
/// </summary>
public static class FontFaces
{
    #region Private Fields

    private static readonly Face[] all =
    [
        new("inter", "Inter", "ms-appx:///Assets/Fonts/Inter.ttf#Inter", Machine: false, ZeroAdvance: 22 / 35.0, LineFactor: 1.21, DisplayAsset: "ms-appx:///Assets/Fonts/InterDisplay.ttf"),
        new("jetbrains-mono", "JetBrains Mono", "ms-appx:///Assets/Fonts/JetBrainsMono.ttf#JetBrains Mono", Machine: true, ZeroAdvance: 0.6, LineFactor: 1.32, DisplayAsset: null),
        new("literata", "Literata", null, Machine: false, ZeroAdvance: 0, LineFactor: 0, DisplayAsset: null),
        new("atkinson-hyperlegible-next", "Atkinson Hyperlegible Next", null, Machine: false, ZeroAdvance: 0, LineFactor: 0, DisplayAsset: null),
    ];

    #endregion

    #region Public Methods

    public static bool IsKnown(string id) => all.Any(face => face.Id == id);

    /// <summary>The face a theme's id names, as it is drawn now: the bundled face of the same kind when its own is not bundled yet.</summary>
    public static Face Drawn(string id)
    {
        var named = all.Single(face => face.Id == id);

        return named.Asset is not null ? named : all.First(face => face.Asset is not null && face.Machine == named.Machine);
    }

    #endregion

    #region Types

    /// <summary>
    /// One face. <paramref name="ZeroAdvance"/> is the width of a zero in ems, which the measure counts in, and
    /// <paramref name="LineFactor"/> the height the face asks of a line, in ems, which centers its letters in a line height,
    /// and <paramref name="DisplayAsset"/> the file of its display cut for large headings, when it has one.
    /// </summary>
    public sealed record Face(string Id, string Family, string? Asset, bool Machine, double ZeroAdvance, double LineFactor, string? DisplayAsset);

    #endregion
}
