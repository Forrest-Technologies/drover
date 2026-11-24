namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// A token the native client asks for by name must exist when it asks, or the screen that asks cannot be drawn. The
/// names come from three places: <c>Tokens.xaml</c>, <c>Styles.xaml</c>, and what a theme writes when it is applied.
/// This holds every name written in the client's source to one of them, so a theme cannot leave a name behind that
/// only the device would find out about.
/// </summary>
public sealed partial class TokenNamesInUseTests
{
    #region Private Fields

    private static readonly XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Written by ThemeTokens in code, apart from the numbers and colors a theme resolves to.
    private static readonly string[] writtenByThemeTokens =
    [
        "DvFontUi", "DvFontMono", "DvFontReading", "DvFontNumerals", "DvFontHeading", "DvFontHeadingLarge",
        "DvLeadIn14", "DvLeadOut14", "DvLeadInMono", "DvLeadOutMono",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_token_name_in_the_clients_source_exists_when_it_is_asked_for()
    {
        var known = Known();
        var missing = from file in Repository.NativeFiles(".cs", ".xaml")
                      from match in TokenName().Matches(file.Text).Cast<Match>()
                      let name = match.Groups[1].Value
                      where !known.Contains(name)
                      select $"{file.Path}: {name}";

        missing.Distinct().Should()
            .BeEmpty("a token is in Tokens.xaml, Styles.xaml, or written by a theme when it is applied");
    }

    [Fact]
    public void Every_token_a_theme_resolves_has_a_name_that_is_not_taken_by_another()
    {
        var resolved = BuiltInThemes.NightYard.Resolve(ColorMode.Dark);
        var names = resolved.Colors.Keys.Concat(resolved.Numbers.Keys).Select(TokenNames.Of).ToList();

        names.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Private Methods

    private static HashSet<string> Known()
    {
        var resolved = BuiltInThemes.NightYard.Resolve(ColorMode.Dark);
        var styles = XDocument.Load(Path.Combine(Repository.NativeFolder, "Theme", "Styles.xaml")).Descendants()
            .Select(element => (string?)element.Attribute(xaml + "Key")).OfType<string>();
        var colors = resolved.Colors.Keys.Select(TokenNames.Of).SelectMany(name => (string[])[name, $"{name}Brush"]);

        return
        [
            .. NativeTokens.Values().Keys,
            .. NativeTokens.Colors("Default").Keys,
            .. NativeTokens.Brushes("Default").Keys,
            .. styles,
            .. colors,
            .. resolved.Numbers.Keys.Select(TokenNames.Of),
            .. writtenByThemeTokens,
        ];
    }

    [GeneratedRegex("\"(Dv[A-Z][A-Za-z0-9]*)\"")]
    private static partial Regex TokenName();

    #endregion
}
