namespace Drover.Native.Theme;

/// <summary>Reads the text styles and control templates of <c>Theme/Styles.xaml</c> from code.</summary>
public static class Styles
{
    #region Private Fields

    private static ResourceDictionary? dictionary;

    #endregion

    #region Properties

    /// <summary>The style file, loaded when first asked for and merged into the application's resources by the app.</summary>
    public static ResourceDictionary Dictionary => dictionary ??= Load();

    #endregion

    #region Public Methods

    /// <summary>A line of text in one of the text styles: <c>Styles.Text("Sign in", "DvTextH1")</c>.</summary>
    public static TextBlock Text(string? text, string style = "DvTextBody") =>
        new() { Text = text ?? string.Empty, Style = (Style)Dictionary[style] };

    public static ControlTemplate Template(string key) => (ControlTemplate)Dictionary[key];

    /// <summary>
    /// Loads the style file again, so its styles take the numbers a newly applied theme wrote into the token file,
    /// and puts the new dictionary where the old one was in the application's resources. A screen made after this
    /// has the new styles; one made before keeps what it was made with.
    /// </summary>
    public static void Reload()
    {
        var old = dictionary;
        dictionary = Load();

        var merged = Application.Current?.Resources.MergedDictionaries;
        if (old is not null && merged is not null && merged.IndexOf(old) is var index and >= 0)
        {
            merged[index] = dictionary;
        }
    }

    #endregion

    #region Private Methods

    private static ResourceDictionary Load() => new() { Source = new Uri("ms-appx:///Theme/Styles.xaml") };

    #endregion
}
