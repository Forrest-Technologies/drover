namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// Proves that shared native text input templates expose a full-bounds hit-testable surface, multiline text areas
/// stretch their scroll viewer across the full input height, and the live pointer-tap focus check is omitted from
/// shipping builds.
/// </summary>
public sealed class MultilineInputFocusTests
{
    #region Public Methods

    [Theory]
    [InlineData("DvTextBoxTemplate")]
    [InlineData("DvPasswordBoxTemplate")]
    public void Text_input_template_wraps_content_element_in_a_transparent_hit_testable_root(string templateKey)
    {
        var styles = Repository.NativeFiles(".xaml").Single(file => file.Path == "Theme/Styles.xaml").Text;
        var document = XDocument.Parse(styles);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document.Descendants()
            .Single(element => element.Name.LocalName == "ControlTemplate"
                && (string?)element.Attribute(xaml + "Key") == templateKey);

        var root = template.Elements().Should().ContainSingle().Subject;
        root.Name.LocalName.Should().Be("Grid", "the control template needs a hit-testable panel covering the whole input");
        ((string?)root.Attribute("Background")).Should().Be(
            "Transparent",
            "a panel with no background is hit-test invisible and lets taps fall through to the frame behind it");

        var contentElement = root.Elements()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "ContentElement");
        contentElement.Name.LocalName.Should().Be("ScrollViewer");
    }

    [Fact]
    public void Multiline_text_area_stretches_its_content_element_and_uses_the_shared_text_box_template()
    {
        var textArea = Repository.NativeFiles(".cs").Single(file => file.Path == "Controls/TextArea.cs").Text;

        textArea.Should().Contain("Template = Styles.Template(\"DvTextBoxTemplate\")");
        textArea.Should().Contain("AcceptsReturn = true");
        textArea.Should().Contain("TextWrapping = TextWrapping.Wrap");
        textArea.Should().Contain("VerticalContentAlignment = VerticalAlignment.Stretch");
        textArea.Should().NotContain("VerticalContentAlignment = VerticalAlignment.Top");
    }

    [Fact]
    public void Edit_sheet_instructions_use_the_shared_multiline_input()
    {
        var page = Repository.NativeFiles(".cs").Single(file => file.Path == "Pages/Projects/ProjectPage.cs").Text;
        var area = Repository.NativeFiles(".cs").Single(file => file.Path == "Controls/TextArea.cs").Text;

        page.Should().Contain("TextArea instructionsField");
        page.Should().Contain("Rows = 8");
        page.Should().Contain("\"ProjectInstructionsField\"");
        page.Should().NotContain("new TextBox");
        area.Should().Contain("Template = Styles.Template(\"DvTextBoxTemplate\")");
        area.Should().Contain("AcceptsReturn = true");
        area.Should().Contain("VerticalContentAlignment = VerticalAlignment.Stretch");
    }

    [Fact]
    public void Shipping_builds_omit_multiline_input_focus_check_and_launch_hooks()
    {
        var nativeRoot = Path.Combine(Repository.Root, "src", "Drover.Native");
        var project = File.ReadAllText(Path.Combine(nativeRoot, "Drover.Native.csproj"));
        var app = File.ReadAllText(Path.Combine(nativeRoot, "App.xaml.cs"));
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "native.yml"));

        project.Should().Contain("""<PropertyGroup Condition="'$(DroverMultilineFocusCheck)' == 'true'">""");
        project.Should().Contain("""<ItemGroup Condition="'$(DroverMultilineFocusCheck)' != 'true'">""");
        project.Should().Contain("""<Compile Remove="Controls/MultilineInputFocusCheck.cs" />""");
        app.Should().Contain("#if DROVER_MULTILINE_FOCUS_CHECK");
        workflow.Should().Contain("DroverMultilineFocusCheck=true");
        workflow.Should().Contain("MultilineInputFocusCheck");
    }

    #endregion
}
