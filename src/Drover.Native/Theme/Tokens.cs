namespace Drover.Native.Theme;

/// <summary>
/// Reads the design tokens of <c>Theme/Tokens.xaml</c> from code. A control that builds its own visuals asks here
/// for a value by its token name, in the theme of the element it is drawing, so no raw value is written anywhere
/// but the token file. A page written in XAML does not need this: it uses <c>{ThemeResource DvTextBrush}</c>.
/// </summary>
public static class Tokens
{
    #region Private Fields

    private const string DarkKey = "Default";

    private const string LightKey = "Light";

    private static ResourceDictionary? dictionary;

    #endregion

    #region Properties

    /// <summary>The token file, loaded once and merged into the application's resources by the app.</summary>
    public static ResourceDictionary Dictionary =>
        dictionary ??= new ResourceDictionary { Source = new Uri("ms-appx:///Theme/Tokens.xaml") };

    public static FontFamily FontUi => (FontFamily)Dictionary["DvFontUi"];

    public static FontFamily FontMono => (FontFamily)Dictionary["DvFontMono"];

    #endregion

    #region Public Methods

    /// <summary>A length, a size, or a duration in milliseconds: <c>Tokens.Number("DvSpace4")</c>.</summary>
    public static double Number(string key) => (double)Dictionary[key];

    /// <summary>A color token's brush in the theme <paramref name="scope"/> is shown in: <c>Tokens.Brush(this, "DvBg2")</c>.</summary>
    public static Brush Brush(FrameworkElement scope, string key) => (Brush)Themed(scope)[$"{key}Brush"];

    public static Color Color(FrameworkElement scope, string key) => (Color)Themed(scope)[key];

    public static Thickness Space(string horizontal, string vertical) => new(Number(horizontal), Number(vertical), Number(horizontal), Number(vertical));

    #endregion

    #region Private Methods

    private static ResourceDictionary Themed(FrameworkElement scope) =>
        (ResourceDictionary)Dictionary.ThemeDictionaries[scope.ActualTheme is ElementTheme.Light ? LightKey : DarkKey];

    #endregion
}
