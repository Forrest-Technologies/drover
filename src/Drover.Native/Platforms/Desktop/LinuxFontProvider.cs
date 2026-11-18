namespace Drover.Native;

internal sealed class LinuxFontProvider(IFontProvider skia) : IFontProvider
{
    #region Private Fields

    private readonly Lazy<IFontFile> bundledDefault = new(() => LoadBundledDefault(skia));

    #endregion

    #region Interface Implementations

    public IFont? CreateFont(
        byte[] data, string? familyNameHint, FontWeight weight, FontStretch stretch, FontStyle style, float fontSize) =>
        skia.CreateFont(data, familyNameHint, weight, stretch, style, fontSize);

    public IFontFile LoadFontFile(Stream stream) => skia.LoadFontFile(stream);

    public IFont? MatchFamily(string familyName, FontWeight weight, FontStretch stretch, FontStyle style, float fontSize) =>
        skia.MatchFamily(familyName, weight, stretch, style, fontSize);

    public ValueTask<IFont?> MatchFamilyAsync(
        string familyName, FontWeight weight, FontStretch stretch, FontStyle style, float fontSize) =>
        skia.MatchFamilyAsync(familyName, weight, stretch, style, fontSize);

    public ValueTask<IFont?> MatchCharacterAsync(
        int codepoint, FontWeight weight, FontStretch stretch, FontStyle style, float fontSize) =>
        skia.MatchCharacterAsync(codepoint, weight, stretch, style, fontSize);

    // Skia can return an empty system default on Linux while Uno loads the requested font.
    public IFont GetDefaultFont(FontWeight weight, FontStretch stretch, FontStyle style, float fontSize) =>
        bundledDefault.Value.CreateFont(null, weight, stretch, style, fontSize)
        ?? throw new InvalidOperationException("The bundled Inter fallback font could not be read.");

    #endregion

    #region Private Methods

    private static IFontFile LoadBundledDefault(IFontProvider skia)
    {
        using var stream = typeof(LinuxFontProvider).Assembly.GetManifestResourceStream("Drover.Native.FallbackInter.ttf")
            ?? throw new InvalidOperationException("The bundled Inter fallback font is missing.");
        return skia.LoadFontFile(stream);
    }

    #endregion
}
