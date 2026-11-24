namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// The rules of DESIGN.md that can be checked in the native client's source, as the web client's are checked in
/// its markup and styles: no raw value outside the token files, a name on every control that shows only an icon,
/// nothing that loops outside the four live states, and no other product named.
/// </summary>
public sealed partial class NativeInterfaceRuleTests
{
    #region Private Fields

    // The two files that may hold raw values: the tokens, and the styles that restate token products.
    private static readonly string[] valueFiles = ["Theme/Tokens.xaml", "Theme/Styles.xaml"];

    // The only places a clock ticks or an animation repeats: the four live states and the motion they share.
    private static readonly string[] liveStateFiles =
    [
        "Theme/Motion.cs", "Controls/AvatarCanvas.cs", "Controls/TravelLine.cs", "Controls/LiveLight.cs",
    ];

    private static readonly string[] sizeAttributes =
    [
        "Margin", "Padding", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "FontSize", "Spacing",
        "RowSpacing", "ColumnSpacing", "CornerRadius", "BorderThickness", "LineHeight", "StrokeThickness",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void No_color_is_written_outside_the_token_file()
    {
        var found = from file in Repository.NativeFiles(".xaml", ".cs")
                    where file.Path != "Theme/Tokens.xaml"
                    from match in RawColor().Matches(file.Text).Cast<Match>()
                    select $"{file.Path}: {match.Value}";

        found.Should().BeEmpty("a color is a token: Tokens.Brush in code, a ThemeResource brush in markup");
    }

    [Fact]
    public void No_named_color_but_transparent_is_used_in_code()
    {
        var found = from file in Repository.NativeFiles(".cs")
                    from match in NamedColor().Matches(file.Text).Cast<Match>()
                    where match.Groups[1].Value != "Transparent"
                    select $"{file.Path}: {match.Value}";

        found.Should().BeEmpty();
    }

    [Fact]
    public void No_size_is_written_as_a_number_in_markup_outside_the_token_and_style_files()
    {
        var found = from file in Repository.NativeFiles(".xaml")
                    where !valueFiles.Contains(file.Path)
                    from element in XDocument.Parse(file.Text).Descendants()
                    from attribute in element.Attributes()
                    where sizeAttributes.Contains(attribute.Name.LocalName) && !attribute.Value.StartsWith('{') && attribute.Value.Any(char.IsDigit)
                    select $"{file.Path}: {element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\"";

        found.Should().BeEmpty("a size is a token, named through StaticResource");
    }

    [Fact]
    public void No_size_or_duration_is_written_as_a_number_where_code_lays_things_out()
    {
        var found = from file in Repository.NativeFiles(".cs")
                    where !liveStateFiles.Contains(file.Path) && file.Path != "Controls/Icon.cs" && file.Path != "Controls/DroverMark.cs"
                    from match in RawLayoutNumber().Matches(file.Text).Cast<Match>()
                    select $"{file.Path}: {match.Value.Trim()}";

        found.Should().BeEmpty("a size is a token: Tokens.Number(\"DvSpace4\")");
    }

    [Fact]
    public void Every_icon_button_has_a_label_which_is_its_accessible_name()
    {
        var inMarkup = from file in Repository.NativeFiles(".xaml")
                       from element in XDocument.Parse(file.Text).Descendants()
                       where element.Name.LocalName == "IconButton" && string.IsNullOrWhiteSpace((string?)element.Attribute("Label"))
                       select $"{file.Path}: an IconButton has no Label";

        var inCode = from file in Repository.NativeFiles(".cs")
                     from match in IconButtonInitializer().Matches(file.Text).Cast<Match>()
                     where !match.Groups[1].Value.Contains("Label =", StringComparison.Ordinal)
                     select $"{file.Path}: new IconButton {{{match.Groups[1].Value.Trim()}}} has no Label";

        inMarkup.Concat(inCode).Should().BeEmpty("an icon alone has no name");
    }

    [Fact]
    public void The_icon_button_refuses_to_be_shown_without_a_label()
    {
        var source = Repository.NativeFiles(".cs").Single(file => file.Path == "Controls/IconButton.cs").Text;

        source.Should().Contain("string.IsNullOrWhiteSpace(Label)").And.Contain("throw new InvalidOperationException");
    }

    [Fact]
    public void Nothing_repeats_or_ticks_outside_the_four_live_states()
    {
        var inCode = from file in Repository.NativeFiles(".cs")
                     where !liveStateFiles.Contains(file.Path)
                     from match in Looping().Matches(file.Text).Cast<Match>()
                     select $"{file.Path}: {match.Value}";

        var inMarkup = from file in Repository.NativeFiles(".xaml")
                       where file.Text.Contains("RepeatBehavior", StringComparison.Ordinal)
                       select $"{file.Path}: RepeatBehavior";

        inCode.Concat(inMarkup).Should().BeEmpty("if nothing is happening, nothing moves: motion comes from Motion, TravelLine, LiveLight, and the avatar");
    }

    [Fact]
    public void The_interface_names_no_other_product()
    {
        var found = from file in Repository.NativeFiles(".xaml", ".cs")
                    from match in OtherProduct().Matches(Literals(file.Text)).Cast<Match>()
                    select $"{file.Path}: {match.Value}";

        found.Should().BeEmpty();
    }

    [Fact]
    public void No_interface_text_is_filler_or_hype()
    {
        var found = from file in Repository.NativeFiles(".xaml", ".cs")
                    from match in Filler().Matches(Literals(file.Text)).Cast<Match>()
                    select $"{file.Path}: {match.Value}";

        found.Should().BeEmpty();
    }

    [Fact]
    public void Every_control_that_draws_itself_as_decoration_is_left_out_of_what_assistive_technology_reads()
    {
        string[] decorations = ["Controls/Icon.cs", "Controls/DroverMark.cs", "Controls/Yard.cs", "Controls/TravelLine.cs", "Controls/LiveLight.cs", "Controls/AvatarCanvas.cs"];

        var named = Repository.NativeFiles(".cs")
            .Where(file => decorations.Contains(file.Path) && !file.Text.Contains("AccessibilityView.Raw", StringComparison.Ordinal))
            .Select(file => file.Path);

        named.Should().BeEmpty();
    }

    [Fact]
    public void Lists_of_things_to_open_use_the_list_card()
    {
        string[] listFiles =
        [
            "Pages/Projects/ProjectListPage.cs", "Pages/Bots/Parts/CrewRow.cs", "Pages/Extensions/ExtensionRow.cs",
        ];
        var found = from file in Repository.NativeFiles(".cs")
                    where listFiles.Contains(file.Path)
                    from match in OwnFrameOrLink().Matches(file.Text).Cast<Match>()
                    select $"{file.Path}: {match.Value.Trim()}";

        found.Should().BeEmpty("one card draws every list of things to open");
    }

    [Fact]
    public void A_page_that_draws_its_own_frame_or_link_is_caught()
    {
        const string Sample = """
            var frame = new Facet();
            Facet other = new();
            var link = new Link { Text = "A project" };
            Link another = new();
            """;

        OwnFrameOrLink().Count(Sample).Should().Be(4);
    }

    #endregion

    #region Private Methods

    [GeneratedRegex(@"new\s+Facet\b|\bFacet\s+\w+\s*=\s*new\s*\(|new\s+Link\b|\bLink\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex OwnFrameOrLink();

    // The text a person can see: what is between quotes. Comments and names are not interface text.
    private static string Literals(string source) => string.Join('\n', StringLiteral().Matches(source).Select(match => match.Value));

    [GeneratedRegex(@"#[0-9A-Fa-f]{8}\b|#[0-9A-Fa-f]{6}\b|Color\.FromArgb\(\s*\d|new SKColor\(\s*\d")]
    private static partial Regex RawColor();

    [GeneratedRegex(@"\b(?:Microsoft\.UI\.)?Colors\.([A-Z][A-Za-z]+)\b|SKColors\.([A-Z][A-Za-z]+)")]
    private static partial Regex NamedColor();

    // A layout property set straight to a number: Width = 12, Spacing = 8, new Thickness(4), FontSize = 14.
    [GeneratedRegex(@"\b(?:Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|Spacing|RowSpacing|ColumnSpacing|FontSize|LineHeight|StrokeThickness)\s*=\s*[1-9]\d*(?:\.\d+)?\s*[,;}]|new Thickness\(\s*[1-9]|new CornerRadius\(\s*[1-9]|TimeSpan\.FromMilliseconds\(\s*\d")]
    private static partial Regex RawLayoutNumber();

    [GeneratedRegex(@"new IconButton\s*\{([^}]*)\}")]
    private static partial Regex IconButtonInitializer();

    [GeneratedRegex(@"RepeatBehavior|DispatcherTimer|DispatcherQueueTimer|CompositionTarget\.Rendering")]
    private static partial Regex Looping();

    [GeneratedRegex("\"(?:[^\"\\\\\\n]|\\\\.)*\"")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\b(ChatGPT|OpenAI|Claude|Gemini|Copilot|Slack|Discord|Teams|Uno Platform|WinUI|Blazor|MAUI)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherProduct();

    [GeneratedRegex(@"\b(no data|coming soon|sorry|oops|powerful|seamless|supercharge|magic|next-gen|AI-powered)\b|!""", RegexOptions.IgnoreCase)]
    private static partial Regex Filler();

    #endregion
}
