namespace Drover.Native.Theme;

/// <summary>Reads the text styles and control templates of <c>Theme/Styles.xaml</c> from code.</summary>
public static class Styles
{
    #region Private Fields

    private static ResourceDictionary? dictionary;

    #endregion

    #region Properties

    /// <summary>The style file, loaded once and merged into the application's resources by the app.</summary>
    public static ResourceDictionary Dictionary =>
        dictionary ??= new ResourceDictionary { Source = new Uri("ms-appx:///Theme/Styles.xaml") };

    #endregion

    #region Public Methods

    /// <summary>A line of text in one of the text styles: <c>Styles.Text("Sign in", "DvTextH1")</c>.</summary>
    public static TextBlock Text(string? text, string style = "DvTextBody") =>
        new() { Text = text ?? string.Empty, Style = (Style)Dictionary[style] };

    public static ControlTemplate Template(string key) => (ControlTemplate)Dictionary[key];

    #endregion
}
