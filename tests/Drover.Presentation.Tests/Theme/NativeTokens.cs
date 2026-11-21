namespace Drover.Presentation.Tests.Theme;

/// <summary>Reads the native client's token file and the web client's, so the two can be held against each other.</summary>
public static partial class NativeTokens
{
    #region Private Fields

    private static readonly XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly XDocument document = XDocument.Load(Repository.NativeTokens);

    private static readonly string css = File.ReadAllText(Repository.TokenSpecification);

    #endregion

    #region Public Methods

    /// <summary>The name a web token has in the native file: "bg-0" is "DvBg0".</summary>
    public static string NameOf(string token) =>
        $"Dv{string.Concat(token.Split('-').Select(part => $"{char.ToUpperInvariant(part[0])}{part[1..]}"))}";

    /// <summary>The colors of one theme dictionary of the native file, by key: "Default", "Dark", or "Light".</summary>
    public static Dictionary<string, string> Colors(string theme) =>
        ThemeDictionary(theme).Elements().Where(element => element.Name.LocalName == "Color").ToDictionary(Key, element => element.Value.Trim());

    /// <summary>The brushes of one theme dictionary, by key, each with the color resource it points at.</summary>
    public static Dictionary<string, string> Brushes(string theme) =>
        ThemeDictionary(theme).Elements().Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(Key, element => (string)element.Attribute("Color")!);

    /// <summary>The values outside the theme dictionaries, by key.</summary>
    public static Dictionary<string, string> Values() =>
        document.Root!.Elements().Where(element => element.Attribute(xaml + "Key") is not null).ToDictionary(Key, element => element.Value.Trim());

    /// <summary>The web client's tokens for the dark theme, by name without the "--dv-" prefix.</summary>
    public static Dictionary<string, string> WebDark() => Declarations(Block(":root,\n[data-theme=\"dark\"]"));

    /// <summary>The web client's tokens for the light theme: the dark ones, with what the light theme changes.</summary>
    public static Dictionary<string, string> WebLight()
    {
        var light = WebDark();
        foreach (var (name, value) in Declarations(Block("[data-theme=\"light\"]")))
        {
            light[name] = value;
        }

        return light;
    }

    #endregion

    #region Private Methods

    private static string Key(XElement element) => (string)element.Attribute(xaml + "Key")!;

    private static XElement ThemeDictionary(string theme) =>
        document.Root!.Elements().Single(element => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries")
            .Elements().Single(element => Key(element) == theme);

    private static string Block(string selector)
    {
        var start = css.IndexOf($"{selector} {{", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"tokens.css opens a block for {selector}");
        return css[start..css.IndexOf('}', start)];
    }

    private static Dictionary<string, string> Declarations(string block) =>
        Declaration().Matches(block).ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Trim());

    [GeneratedRegex(@"--dv-([a-z0-9-]+):\s*([^;]+);")]
    private static partial Regex Declaration();

    #endregion
}
