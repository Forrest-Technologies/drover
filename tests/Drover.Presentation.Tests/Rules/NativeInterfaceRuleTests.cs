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

    // The card methods, including the helpers a card draws a tool or a catalog row with.
    private static readonly string[] cardMethodMarkers = ["Connection(Drover.", "ToolReview(", "CatalogRow("];

    // Each dock entry opens the screen whose own icon it shows.
    private static readonly Dictionary<string, string> dockIcons = new()
    {
        ["Chats"] = "Conversation",
        ["Projects"] = "Project",
        ["Bots"] = "Crew",
        ["Tools"] = "Tool",
    };

    // Bars keep a finger's height because they are not controls; the two chat files move in #735.
    private static readonly (string Path, string Reason)[] touchHeightExceptions =
    [
        ("Layouts/WorkLayout.cs", "the top bar and its home link are a bar, not a control"),
        ("Layouts/NavLink.cs", "the links of the top bar are a bar, not controls"),
        ("Session/AccountMenu.cs", "the account menu trigger belongs to the top bar"),
        ("Chat/NativeFilePreviewControl.cs", "conversation previews move to Targets.Minimum in #735"),
    ];

    // Only forms, editors and the screen's one decision may carry a primary button.
    private static readonly (string Path, string Reason)[] primaryFiles =
    [
        ("Controls/Button.cs", "the control paints its own primary kind"),
        ("Controls/EmptyState.cs", "the empty state action is the screen's one primary"),
        ("Session/AboutDrover.cs", "session dialog"),
        ("Session/AccountMenu.cs", "session dialog, install update"),
        ("Session/PushControl.cs", "session dialog, turn on notifications"),
        ("Session/RetiredAppRequest.cs", "session dialog, uninstall the retired app"),
        ("Pages/Dev/", "gallery samples are not screens"),
        ("Device/ConnectPage.cs", "setup"),
        ("Pages/SignInPage.xaml", "sign-in"),
        ("Pages/Setup/", "setup"),
        ("Pages/Projects/ProjectListPage.cs", "Projects create form"),
        ("Pages/Projects/ProjectPage.cs", "Projects save bar"),
        ("Pages/Models/ModelConnectionsPage.cs", "Models sheets"),
        ("Pages/Tools/ToolConnectionsPage.cs", "Tools sheets and dialogs"),
        ("Pages/ImportForm.cs", "bot and extension import form"),
        ("Pages/Bots/BotEditorPage.xaml.cs", "bot editor save bar"),
        ("Pages/Bots/Parts/RuleForm.cs", "bot editor rule form"),
        ("Pages/Bots/Parts/RoutinesSectionView.cs", "bot editor routine form"),
        ("Pages/Bots/Parts/RoutineForm.cs", "bot editor routine form"),
        ("Pages/Bots/Parts/SandboxSectionView.cs", "bot editor sandbox form, #682"),
        ("Pages/Bots/BotSandboxPage.cs", "sandbox page run form"),
        ("Bots/SandboxScreen.cs", "sandbox screen run form, #682"),
        ("Chat/", "conversation pieces and composer, #682 and #735"),
    ];

    // The only places a clock ticks or an animation repeats: the four live states and the motion they share.
    private static readonly string[] liveStateFiles =
    [
        "Theme/Motion.cs", "Controls/AvatarCanvas.cs", "Controls/TravelLine.cs", "Controls/LiveLight.cs",
    ];

    // Every list kept in step with a keyed collection, and what keeps the number of its children small. A list that
    // can grow without end is shown through ListWindow instead of being listed here with a wish.
    private static readonly (string Path, string Bound)[] keyedLists =
    [
        ("Chat/MessageList.cs", "the page of a ListWindow: MessageListViewModel.Shown"),
        ("Chat/ChatEntry.cs", "one message's tool cards and files, and the work of one delegation"),
        ("Chat/WorkLedger.cs", "the steps of one turn"),
        ("Controls/Timeline.cs", "the moments of one run"),
    ];

    private static readonly string[] sizeAttributes =
    [
        "Margin", "Padding", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "FontSize", "Spacing",
        "RowSpacing", "ColumnSpacing", "CornerRadius", "BorderThickness", "LineHeight", "StrokeThickness",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_keyed_list_says_what_bounds_it()
    {
        var found = from file in Repository.NativeFiles(".cs")
                    where file.Path != "Chat/Keyed.cs" && file.Text.Contains("Keyed.Sync(", StringComparison.Ordinal)
                    select file.Path;

        found.Should().BeEquivalentTo(
            keyedLists.Select(list => list.Path),
            "every child of a keyed list is made and laid out, so a list is short by what it holds or is shown through ListWindow (docs/architecture/18-native-client.md, Long lists)");
        keyedLists.Should().OnlyContain(list => list.Bound.Length > 0);
    }

    [Fact]
    public void The_message_list_draws_its_page_and_never_the_whole_conversation()
    {
        var list = Repository.NativeFiles(".cs").Single(file => file.Path == "Chat/MessageList.cs").Text;

        list.Should().Contain("list.Shown");
        list.Should().NotContain("list.Messages", "the whole conversation is the view model's; the page is what is drawn");
    }

    [Fact]
    public void Shipping_builds_omit_the_long_conversation_check_and_ci_runs_it()
    {
        var nativeRoot = Path.Combine(Repository.Root, "src", "Drover.Native");
        var project = File.ReadAllText(Path.Combine(nativeRoot, "Drover.Native.csproj"));
        var app = File.ReadAllText(Path.Combine(nativeRoot, "App.xaml.cs"));
        var workflow = File.ReadAllText(Path.Combine(Repository.Root, ".github", "workflows", "native.yml"));

        project.Should().Contain("""<PropertyGroup Condition="'$(DroverLongConversationCheck)' == 'true'">""");
        project.Should().Contain("""<ItemGroup Condition="'$(DroverLongConversationCheck)' != 'true'">""");
        project.Should().Contain("""<Compile Remove="Chat/LongConversationCheck.cs" />""");
        app.Should().Contain("#if DROVER_LONG_CONVERSATION_CHECK");
        workflow.Should().Contain("DroverLongConversationCheck=true");
        workflow.Should().Contain("Long conversation check passed.");
        Regex.Matches(workflow, "grep -a -q -E '([^']*)'").Should().OnlyContain(
            hooks => hooks.Groups[1].Value.Contains("LongConversationCheck", StringComparison.Ordinal),
            "no shipping build may carry the check");
    }

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
        // The long conversation check times the frames the screen draws while it scrolls. It shows nothing of its
        // own and is not in a shipping build (Shipping_builds_omit_the_long_conversation_check_and_ci_runs_it).
        var inCode = from file in Repository.NativeFiles(".cs")
                     where !liveStateFiles.Contains(file.Path) && file.Path != "Chat/LongConversationCheck.cs"
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
    public void No_provider_is_named_in_client_source()
    {
        var names = KnownProviders.All.SelectMany(row => new[] { row.Id, row.DisplayName }).Distinct().ToList();
        var found = from file in Repository.NativeFiles(".xaml", ".cs").Concat(Repository.PresentationFiles(".cs"))
                    from name in names
                    where file.Text.Contains($"\"{name}\"", StringComparison.Ordinal)
                    select $"{file.Path}: \"{name}\"";

        found.Should().BeEmpty("the names, addresses and key rules of the named providers come from the server");
    }

    [Fact]
    public void No_provider_kind_is_named_in_client_source()
    {
        // The one kind the client names is the compatible one, once, in NamedProviders: a named provider is a
        // starting point for it, and it is the kind that still works when the list of kinds cannot be read.
        var kinds = new[] { "venice", OpenAICompatibleProvider.ConnectionKind };
        var found = from file in Repository.NativeFiles(".xaml", ".cs").Concat(Repository.PresentationFiles(".cs"))
                    from kind in kinds
                    where file.Text.Contains($"\"{kind}\"", StringComparison.Ordinal)
                    where !(kind == OpenAICompatibleProvider.ConnectionKind && file.Path.EndsWith("NamedProviders.cs", StringComparison.Ordinal))
                    select $"{file.Path}: \"{kind}\"";

        found.Should().BeEmpty("the kinds, their names and what each asks for come from the server");
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
    public void Connection_cards_put_their_actions_in_a_menu()
    {
        string[] files = ["Pages/Models/ModelConnectionsPage.cs", "Pages/Tools/ToolConnectionsPage.cs"];
        var found = from path in files
                    let source = Repository.NativeFiles(".cs").Single(file => file.Path == path).Text
                    from button in CardActions(CardMethod(source))
                    select $"{path}: {button}";

        found.Should().BeEmpty("a card's actions belong in its More menu, apart from sign in and accept change");
    }

    [Fact]
    public void A_worded_button_on_a_card_is_caught()
    {
        const string Sample = """
            body.Children.Add(Action("View models", () => model.ShowCatalog(connection)));
            body.Children.Add(new Button { Content = "Test connection" });
            """;

        CardActions(Sample).Should().HaveCount(2);
    }

    [Fact]
    public void A_button_with_a_ternary_label_on_a_card_is_caught()
    {
        const string Sample = """
            body.Children.Add(Action(model.Busy == connection.Id ? "Working" : "Check and list tools", () => model.Refresh(connection)));
            Button other = new();
            """;

        CardActions(Sample).Should().HaveCount(2);
    }

    [Fact]
    public void The_sign_in_and_accept_change_buttons_on_a_card_are_allowed()
    {
        const string Sample = """
            body.Children.Add(Action("Sign in", () => model.SignIn(connection)));
            body.Children.Add(Action("Sign in again", () => model.SignIn(connection)));
            body.Children.Add(Action(connection.SignIn is "needs_reconnect" ? "Sign in again" : "Sign in", () => model.SignIn(connection)));
            body.Children.Add(Action($"Accept the change to {tool.Name}", () => model.Accept(tool)));
            """;

        CardActions(Sample).Should().BeEmpty();
    }

    [Fact]
    public void Controls_take_their_height_from_the_target_size_not_the_touch_token()
    {
        var found = from file in Repository.NativeFiles(".cs", ".xaml")
                    where !file.Path.StartsWith("Pages/Dev/", StringComparison.Ordinal)
                        && AllowedReason(touchHeightExceptions, file.Path) is null
                    from match in TouchHeight().Matches(file.Text).Cast<Match>()
                    select $"{file.Path}: {match.Value.Trim()}";

        found.Should().BeEmpty(
            "a control takes its height from Targets.Minimum, which is 44 for a finger and 32 for a pointer");
    }

    [Fact]
    public void A_control_sized_from_the_touch_token_is_caught()
    {
        const string Sample = """
            toggle.MinHeight = Tokens.Number("DvTargetTouch");
            var link = new Link { MinWidth = Tokens.Number("DvTargetTouch") };
            <dv:Button MinHeight="{StaticResource DvTargetTouch}" />
            <dv:Button MinWidth="{ThemeResource DvTargetTouch}" />
            """;

        TouchHeight().Count(Sample).Should().Be(4);
    }

    [Fact]
    public void Only_forms_and_editors_draw_a_primary_button()
    {
        var found = from file in Repository.NativeFiles(".cs", ".xaml")
                    from button in PrimaryOn(file.Path, file.Text)
                    select $"{file.Path}: {button}";

        found.Should().BeEmpty("at rest a screen has at most one primary action, in a form or an editor");
    }

    [Fact]
    public void A_primary_button_on_a_list_row_is_caught()
    {
        const string Sample = "var turnOn = new Button { Kind = ButtonKind.Primary };";

        PrimaryOn("Pages/Extensions/ExtensionRow.cs", Sample).Should().ContainSingle();
    }

    [Fact]
    public void The_phone_dock_uses_each_screens_own_icon()
    {
        var source = Repository.NativeFiles(".cs").Single(file => file.Path == "Layouts/PhoneNav.cs").Text;

        DockIcons(source).Should().BeEquivalentTo(dockIcons, "the dock uses each screen's own icon");
    }

    [Fact]
    public void A_borrowed_dock_icon_is_caught()
    {
        const string Sample = """
            new NavLink("Chats", Addresses.Home, matchesWhole: true, IconName.Conversation),
            new NavLink("Projects", Addresses.Projects, icon: IconName.Project),
            new NavLink("Bots", Addresses.Bots, icon: IconName.Crew),
            new NavLink("Tools", Addresses.Tools, icon: IconName.Settings),
            """;

        WrongDockIcons(Sample).Should().ContainSingle("a borrowed icon is a counterexample");
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

    [Fact]
    public void Every_work_screen_starts_with_one_screen_header()
    {
        var found = from file in Repository.NativeFiles(".xaml", ".cs")
                    where file.Path.StartsWith("Pages/", StringComparison.Ordinal)
                        && !file.Path.StartsWith("Pages/Dev/", StringComparison.Ordinal)
                    group file by PageOf(file.Path) into page
                    where page.Any(file => file.Text.Contains("WorkLayout", StringComparison.Ordinal))
                    let count = page.Sum(file => HeadersIn(file))
                    let first = page.Any(HeaderLeads)
                    where count != 1 || !first
                    select $"{page.Key}: {count} headers, first {first}";

        found.Should().BeEmpty("one header leads every work screen");
    }

    [Fact]
    public void No_back_control_is_an_underlined_link()
    {
        var words = ViewModelWords();
        var found = from file in Repository.NativeFiles(".xaml", ".cs")
                    where file.Path.StartsWith("Pages/", StringComparison.Ordinal)
                    from link in BackLinks(file, words)
                    select link;

        found.Should().BeEmpty("a way back is an icon button in the screen header, not an underlined link");
    }

    [Fact]
    public void A_screen_header_holds_only_icon_actions()
    {
        List<string> found = [];
        foreach (var file in Repository.NativeFiles(".xaml", ".cs").Where(file => file.Path.StartsWith("Pages/", StringComparison.Ordinal)))
        {
            if (file.Path.EndsWith(".xaml", StringComparison.Ordinal))
            {
                CheckMarkupActions(file, found);
            }
            else
            {
                CheckCodeActions(file, found);
            }
        }

        found.Should().BeEmpty("only an icon button or an icon menu belongs in a screen header");
    }

    [Fact]
    public void A_literal_back_link_in_markup_is_caught()
    {
        const string Markup = "<Link Text=\"Back to projects\" />";
        BackLinksInMarkup("Pages/Sample.xaml", Markup, ViewModelWords()).Should().ContainSingle();
    }

    [Fact]
    public void An_inline_menu_without_an_icon_trigger_is_caught()
    {
        List<string> found = [];
        CheckCodeActions(("Pages/Sample.xaml.cs", "header.Actions.Add(new Menu { Label = \"More\" });"), found);
        found.Should().ContainSingle();
    }

    [Fact]
    public void An_inline_icon_menu_in_a_header_is_allowed()
    {
        List<string> found = [];
        CheckCodeActions(
            ("Pages/Sample.xaml.cs", "header.Actions.Add(new Menu { TriggerIcon = IconName.More, ShowTriggerLabel = false });"),
            found);
        found.Should().BeEmpty();
    }

    [Fact]
    public void A_var_declared_menu_in_a_header_is_checked()
    {
        List<string> found = [];
        CheckCodeActions(
            ("Pages/Sample.xaml.cs", "var menu = new Menu { Label = \"More\" }; header.Actions.Add(menu);"),
            found);
        found.Should().ContainSingle();
    }

    [Fact]
    public void A_var_declared_icon_menu_in_a_header_is_allowed()
    {
        List<string> found = [];
        CheckCodeActions(
            ("Pages/Sample.xaml.cs", "var menu = new Menu { TriggerIcon = IconName.More, ShowTriggerLabel = false }; header.Actions.Add(menu);"),
            found);
        found.Should().BeEmpty();
    }

    [Fact]
    public void An_inserted_inline_menu_in_a_header_is_checked()
    {
        List<string> found = [];
        CheckCodeActions(
            ("Pages/Sample.xaml.cs", "header.Actions.Insert(0, new Menu { Label = \"More\" });"),
            found);
        found.Should().ContainSingle();
    }

    [Fact]
    public void An_inserted_icon_menu_in_a_header_is_allowed()
    {
        List<string> found = [];
        CheckCodeActions(
            ("Pages/Sample.xaml.cs", "header.Actions.Insert(0, new Menu { TriggerIcon = IconName.More, ShowTriggerLabel = false });"),
            found);
        found.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static string? AllowedReason((string Path, string Reason)[] list, string path) =>
        list.FirstOrDefault(item => item.Path.EndsWith('/')
            ? path.StartsWith(item.Path, StringComparison.Ordinal)
            : path == item.Path).Reason;

    private static List<string> PrimaryOn(string path, string text) =>
        AllowedReason(primaryFiles, path) is null
            ? [.. PrimaryButton().Matches(text).Cast<Match>().Select(match => match.Value.Trim())]
            : [];

    private static Dictionary<string, string> DockIcons(string text) =>
        DockIcon().Matches(text).Cast<Match>()
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

    private static List<string> WrongDockIcons(string text)
    {
        var found = DockIcons(text);
        return
        [
            .. dockIcons.Where(expected => !found.TryGetValue(expected.Key, out var icon) || icon != expected.Value)
                .Select(expected => $"{expected.Key}: {(found.TryGetValue(expected.Key, out var icon) ? icon : "missing")}"),
        ];
    }

    [GeneratedRegex(@"\b(?:MinHeight|MinWidth)\s*=\s*(?:Tokens\.Number\(\s*""DvTargetTouch""\s*\)|""\{(?:StaticResource|ThemeResource) DvTargetTouch\}"")")]
    private static partial Regex TouchHeight();

    [GeneratedRegex(@"ButtonKind\.Primary|Kind\s*=\s*""Primary""")]
    private static partial Regex PrimaryButton();

    [GeneratedRegex(@"new(?:\s+NavLink)?\(""(Chats|Projects|Bots|Tools)""[^)]*IconName\.(\w+)\)")]
    private static partial Regex DockIcon();

    private static string CardMethod(string source) =>
        string.Join('\n', cardMethodMarkers.SelectMany(marker => MethodLines(source, marker)));

    private static List<string> MethodLines(string source, string marker)
    {
        var lines = source.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains(marker, StringComparison.Ordinal));
        if (start < 0)
        {
            return [];
        }

        List<string> method = [lines[start]];
        for (var index = start + 1; index < lines.Length; index++)
        {
            method.Add(lines[index]);
            if (lines[index] == "    }")
            {
                break;
            }
        }

        return method;
    }

    private static IEnumerable<string> CardActions(string body) =>
        from match in CardButton().Matches(body).Cast<Match>()
        let label = match.Groups[1].Value
        where !label.Contains("Sign in", StringComparison.Ordinal)
            && !label.Contains("Accept the change to", StringComparison.Ordinal)
        select match.Value.Trim();

    [GeneratedRegex(@"(?:PageParts\.)?Action\(\s*([^,)]+)|new\s+Button\b|\bButton\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex CardButton();

    [GeneratedRegex(@"new\s+Facet\b|\bFacet\s+\w+\s*=\s*new\s*\(|new\s+Link\b|\bLink\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex OwnFrameOrLink();

    private static string PageOf(string path) =>
        path.EndsWith(".xaml.cs", StringComparison.Ordinal) ? path[..^3] : path;

    private static int HeadersIn((string Path, string Text) file) =>
        file.Path.EndsWith(".xaml", StringComparison.Ordinal)
            ? XDocument.Parse(file.Text).Descendants().Count(element => element.Name.LocalName == "ScreenHeader")
            : ScreenHeaderMade().Count(file.Text);

    private static bool HeaderLeads((string Path, string Text) file)
    {
        if (file.Path.EndsWith(".xaml", StringComparison.Ordinal))
        {
            var layout = XDocument.Parse(file.Text).Descendants().FirstOrDefault(element => element.Name.LocalName == "WorkLayout");
            return layout?.Elements().FirstOrDefault()?.Name.LocalName == "ScreenHeader";
        }

        return BodyAdded().Match(file.Text) is { Success: true } added
            && added.Groups[1].Value.Contains("header", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<(string Type, string Member), string> ViewModelWords()
    {
        Dictionary<(string Type, string Member), string> words = [];
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", "Drover.Presentation"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(path);
            var type = ClassName().Match(source).Groups[1].Value;
            foreach (var word in WordValue().Matches(source).Cast<Match>())
            {
                words.TryAdd((type, word.Groups[1].Value), word.Groups[2].Value);
            }
        }

        return words;
    }

    private static IEnumerable<string> BackLinks((string Path, string Text) file, Dictionary<(string Type, string Member), string> words) =>
        file.Path.EndsWith(".xaml", StringComparison.Ordinal)
            ? BackLinksInMarkup(file.Path, file.Text, words)
            : BackLinksInCode(file.Path, file.Text, words);

    private static IEnumerable<string> BackLinksInMarkup(string path, string text, Dictionary<(string Type, string Member), string> words) =>
        from element in XDocument.Parse(text).Descendants()
        where element.Name.LocalName == "Link"
        let said = (string?)element.Attribute("Text") ?? string.Empty
        where SaysBack(said, words)
        select $"{path}: {said}";

    private static IEnumerable<string> BackLinksInCode(string path, string text, Dictionary<(string Type, string Member), string> words) =>
        from made in LinkMade().Matches(text).Cast<Match>()
        let said = LinkText().Match(made.Groups[1].Value).Groups[1].Value.Trim()
        where SaysBack(said, words)
        select $"{path}: {said}";

    private static bool SaysBack(string expression, Dictionary<(string Type, string Member), string> words)
    {
        if (StringLiteral().Match(expression) is { Success: true } literal)
        {
            return literal.Value.Trim('"').StartsWith("Back to", StringComparison.Ordinal);
        }

        var member = MemberRef().Match(expression);
        if (member.Success && words.TryGetValue((member.Groups[1].Value, member.Groups[2].Value), out var said))
        {
            return said.StartsWith("Back to", StringComparison.Ordinal);
        }

        // An attribute in markup has no quotes: Text="Back to projects" arrives here as plain words.
        return expression.StartsWith("Back to", StringComparison.Ordinal);
    }

    private static void CheckMarkupActions((string Path, string Text) file, List<string> found)
    {
        foreach (var header in XDocument.Parse(file.Text).Descendants().Where(element => element.Name.LocalName == "ScreenHeader"))
        {
            foreach (var action in header.Elements())
            {
                if (action.Name.LocalName is "IconButton")
                {
                    continue;
                }

                if (action.Name.LocalName is "Menu" && (string?)action.Attribute("TriggerIcon") is { Length: > 0 }
                    && string.Equals((string?)action.Attribute("ShowTriggerLabel"), "false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                found.Add($"{file.Path}: {action.Name.LocalName} in a screen header");
            }
        }
    }

    private static void CheckCodeActions((string Path, string Text) file, List<string> found)
    {
        foreach (var added in ActionAdded().Matches(file.Text).Cast<Match>())
        {
            var value = ActionIndex().Replace(added.Groups[1].Value.Trim(), string.Empty).Trim();
            var declaration = ActionDeclared().Matches(file.Text).Cast<Match>()
                .FirstOrDefault(decl => decl.Groups[2].Value == value);
            var declared = VarActionDeclared().Matches(file.Text).Cast<Match>()
                .FirstOrDefault(decl => decl.Groups[1].Value == value);
            var type = declaration?.Groups[1].Value ?? declared?.Groups[2].Value
                ?? (value.StartsWith("new IconButton", StringComparison.Ordinal) ? "IconButton"
                    : value.StartsWith("new Menu", StringComparison.Ordinal) ? "Menu" : null);

            if (type is not ("IconButton" or "Menu"))
            {
                found.Add($"{file.Path}: {value} in ScreenHeader.Actions");
                continue;
            }

            if (type is "Menu")
            {
                var initializer = declared?.Groups[3].Value
                    ?? (declaration is null
                        ? InlineMenu().Match(value).Groups[1].Value
                        : ActionMade().Matches(file.Text).Cast<Match>()
                            .FirstOrDefault(made => made.Groups[1].Value == declaration.Groups[2].Value)?.Groups[2].Value ?? string.Empty);
                if (!initializer.Contains("TriggerIcon", StringComparison.Ordinal)
                    || !initializer.Contains("ShowTriggerLabel = false", StringComparison.Ordinal))
                {
                    found.Add($"{file.Path}: the menu in ScreenHeader.Actions needs an icon trigger and no words");
                }
            }
        }
    }

    // The text a person can see: what is between quotes. Comments and names are not interface text.
    private static string Literals(string source) => string.Join('\n', StringLiteral().Matches(source).Select(match => match.Value));

    [GeneratedRegex(@"(?:new\s+ScreenHeader\b|\bScreenHeader\s+\w+\s*=\s*new\s*\()")]
    private static partial Regex ScreenHeaderMade();

    [GeneratedRegex(@"(?:\.Body|\bbody)\.(?:Add|Insert)\(([^)]*)\)")]
    private static partial Regex BodyAdded();

    [GeneratedRegex(@"(?:new\s+Link\s*\{|\bLink\s+\w+\s*=\s*new\s*\(\s*\)\s*\{)([^}]*)\}")]
    private static partial Regex LinkMade();

    [GeneratedRegex(@"Text\s*=\s*([^,}]+)")]
    private static partial Regex LinkText();

    [GeneratedRegex(@"\bclass\s+(\w+)")]
    private static partial Regex ClassName();

    [GeneratedRegex(@"(?:vm:)?([A-Za-z_]\w*)\.(\w+)\b")]
    private static partial Regex MemberRef();

    [GeneratedRegex(@"\b(?:const\s+)?string\s+(\w+)\s*(?:=>|=)\s*\$?""([^""]*)""")]
    private static partial Regex WordValue();

    [GeneratedRegex(@"\.Actions\.(?:Add|Insert)\(([^)]*)\)")]
    private static partial Regex ActionAdded();

    [GeneratedRegex(@"^\d+\s*,\s*")]
    private static partial Regex ActionIndex();

    [GeneratedRegex(@"\b(IconButton|Menu)\s+(\w+)\b")]
    private static partial Regex ActionDeclared();

    [GeneratedRegex(@"\bvar\s+(\w+)\s*=\s*new\s+(IconButton|Menu)\s*\{([^}]*)\}")]
    private static partial Regex VarActionDeclared();

    [GeneratedRegex(@"\b(\w+)\s*=\s*new\s*\(\s*\)\s*\{([^}]*)\}")]
    private static partial Regex ActionMade();

    [GeneratedRegex(@"new\s+Menu\s*\{([^}]*)\}")]
    private static partial Regex InlineMenu();

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
