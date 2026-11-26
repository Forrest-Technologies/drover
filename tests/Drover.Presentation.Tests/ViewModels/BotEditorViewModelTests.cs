namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The bot editor against a stand-in server: the cases of the web client's screen tests, asked of the view model.
/// What it shows at rest, what each section announces, what it sends, and what it says when the server refuses.
/// Every name and value here is a sample.
/// </summary>
public sealed class BotEditorViewModelTests : ViewModelTest
{
    #region Private Fields

    private const string Model = BotEditorViewModel.ModelSection;

    private const string Tools = BotEditorViewModel.ToolsSection;

    private const string Rules = BotEditorViewModel.RulesSection;

    private const string Routines = BotEditorViewModel.RoutinesSection;

    private const string Sandbox = BotEditorViewModel.SandboxSection;

    private const string Team = BotEditorViewModel.TeamSection;

    private const string Limits = BotEditorViewModel.LimitsSection;

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid ruleId = Guid.Parse("0196f1a0-0000-7000-8000-000000000007");

    private static readonly Guid eyesConnectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000008");

    private static readonly Guid madeId = Guid.Parse("0196f1a0-0000-7000-8000-000000000009");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly string bot = $"/api/spaces/{SpaceId}/bots/{botId}";

    #endregion

    #region Constructors

    public BotEditorViewModelTests()
    {
        SignedIn();
        Server.Answer("GET", $"{bot}/definition", Marshal);
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        Server.Answer("GET", $"{space}/model-connections/{connectionId}/models", (ModelCatalogResponse[])[CatalogModel("sample-large", toolCalling: true), CatalogModel("sample-plain", toolCalling: false)]);
        Server.Answer("GET", $"{space}/tool-connections", Array.Empty<ToolConnectionSummary>());
        Server.Answer("GET", $"{bot}/tools", (BotToolEntry[])[TimeTool("allow", "allow", null)]);
        Server.Answer("GET", $"{bot}/schedules", Array.Empty<BotScheduleResponse>());
        Server.Answer("GET", $"{bot}/rules", Array.Empty<BotRuleResponse>());
        Server.Answer("GET", $"{bot}/rules/options", new BotRuleOptions(true));
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: true, MaxIdleMinutes: 60, Desktop: true));
        Server.Answer("GET", $"{bot}/limits/options", new BotRunLimitOptions(MaxSteps: null, MaxTokens: 5_000_000, MaxDelegations: 49));
        Server.Answer("GET", $"{space}/bots/{Guid.Empty}/limits/options", new BotRunLimitOptions(MaxSteps: null, MaxTokens: 5_000_000, MaxDelegations: 49));
    }

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", "sample-large", [], null, null);

    private static ModelConnectionSummary EyesConnection { get; } =
        new(eyesConnectionId, "Eyes connection", "openai_compatible", "https://models.example.test/v1", true, "connected", "sample-eyes", [], null, null);

    private static BotDetail Marshal { get; } = new(
        botId,
        "Marshal",
        "marshal",
        new BotAvatarBody(null, null),
        "Sample purpose",
        "Sample instructions",
        connectionId,
        "sample-large",
        new ModelSettingsBody(null, null, null, "off", false),
        new MemorySettingsBody(false, "none", null, null),
        "none",
        new SandboxSettingsBody("off", [], null),
        [],
        "active",
        null,
        0,
        new BuildLimitsBody(null, null),
        new RunLimitsBody(null, null, null),
        Person.UserId,
        null);

    #endregion

    #region At Rest

    [Fact]
    public async Task The_editor_shows_a_new_space_ceiling_on_a_previously_unlimited_bot()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { RunLimits = new RunLimitsBody(null, null, null, true) });
        var editor = await OpenEditor();
        editor.Draft!.UnlimitedTokens.Should().BeFalse();
        editor.Dirty.Should().BeFalse();
        editor.Summary(Limits).Should().Contain("5,000,000 tokens");
    }

    [Fact]
    public async Task Removing_a_bot_with_bots_beneath_asks_whether_they_go_too_and_sends_the_answer()
    {
        var helper = new BotSummary(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1"), "Sample helper", "sample-helper", string.Empty, connectionId, "sample-large", "active", botId);
        var deeper = helper with { Id = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2"), Name = "Sample deeper", ParentBotId = helper.Id };
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[helper, deeper]);
        Server.Answer("DELETE", $"{space}/bots/{botId}", HttpStatusCode.NoContent);
        var editor = await OpenEditor();

        editor.AskToRemove();

        editor.Removing.Should().BeTrue();
        editor.RemoveTitle.Should().Be("Remove Marshal");
        editor.HasBeneath.Should().BeTrue();
        editor.BeneathWords.Should().Be("2 bots work beneath it. Remove them too, or keep them? Kept bots move up under the top of the crew.");
        string[] labels = [BotEditorViewModel.KeepBot, BotEditorViewModel.RemoveKeepRest, editor.RemoveAllWords];
        labels.Should().Equal("Keep bot", "Remove it, keep the rest", "Remove all 3");

        await editor.Remove(false);

        Server.Addresses.Should().Contain($"DELETE {space}/bots/{botId}?beneath=keep");
        Navigator.Current.Should().Be(Addresses.Bots);
        Toasts.Toasts.Select(toast => toast.Message).Should().Contain("Marshal was removed.");
    }

    [Fact]
    public async Task Removing_a_bot_with_nothing_beneath_asks_nothing_more()
    {
        var editor = await OpenEditor();

        editor.AskToRemove();

        editor.HasBeneath.Should().BeFalse();
        string[] labels = [BotEditorViewModel.KeepBot, BotEditorViewModel.RemoveBot];
        labels.Should().Equal("Keep bot", "Remove bot");
    }

    [Fact]
    public async Task The_editor_shows_who_the_bot_is_and_eight_closed_sections_each_with_its_summary_in_its_name()
    {
        var editor = await OpenEditor();

        editor.State.Should().Be(BotEditorState.Shown);
        editor.Heading.Should().Be("Edit Marshal");
        editor.ShownName.Should().Be("Marshal");
        string[] labels = [BotEditorViewModel.PurposeLabel, BotEditorViewModel.InstructionsLabel];
        labels.Should().Equal("Purpose", "Instructions");
        BotEditorViewModel.Sections.Should().OnlyContain(section => !editor.IsOpen(section));
        BotEditorViewModel.Sections.Select(section => $"{section}, {editor.Summary(section)}").Should().Equal(
            "Model, Sample connection · sample-large",
            "Tools, 1 allowed",
            "Rules, No rules",
            "Memory, Does not remember · keeps every message",
            "Routines, No routines",
            "Sandbox, No sandbox",
            "Team, The space's caps",
            "Limits, No limit · 5,000,000 tokens · 49 handoffs");
        editor.ShowsSaveBar.Should().BeFalse("there is nothing to save yet");
        editor.SaveWords.Should().Be("Nothing to save");
    }

    [Fact]
    public async Task A_section_opens_from_its_button_and_says_so_to_assistive_technology()
    {
        var editor = await OpenEditor();

        editor.Toggle(Model, true);

        editor.IsOpen(Model).Should().BeTrue();
        string[] labels = [ModelSectionViewModel.ConnectionLabel, ModelSectionViewModel.ModelLabel, ModelSectionViewModel.TemperatureLabel];
        labels
            .Should().Equal("Connection", "Model", "Temperature");
        editor.ModelPart.ModelChosenFromList.Should().BeTrue();
    }

    [Fact]
    public async Task A_link_about_memory_opens_the_editor_with_the_memory_section_open()
    {
        var editor = Make<BotEditorViewModel>();

        await editor.Open(botId, "memory");

        BotEditorViewModel.Sections.Where(editor.IsOpen).Should().Equal(BotEditorViewModel.MemorySection);
    }

    [Fact]
    public async Task A_link_about_limits_opens_the_editor_with_the_limits_section_open_and_marked_to_scroll()
    {
        var editor = Make<BotEditorViewModel>();

        await editor.Open(botId, "limits");

        BotEditorViewModel.Sections.Where(editor.IsOpen).Should().Equal(BotEditorViewModel.LimitsSection);
        editor.TakeOpenedSection().Should().Be(BotEditorViewModel.LimitsSection);
        editor.TakeOpenedSection().Should().BeNull("scrolling to the named section happens once");
    }

    #endregion

    #region Model And Tools

    [Fact]
    public async Task Only_the_settings_the_model_has_are_shown()
    {
        var editor = await OpenEditor();
        editor.Toggle(Model, true);
        var part = editor.ModelPart;

        part.ReasoningOptions.Should().NotBeNull();
        part.StructuredOutputShown.Should().BeTrue();
        part.WebSearchShown.Should().BeTrue();
        string[] labels = [ModelSectionViewModel.ReasoningLabel, ModelSectionViewModel.StructuredOutputLabel, ModelSectionViewModel.WebSearchLabel];
        labels
            .Should().Equal("Reasoning effort", "Answer as a JSON object", "Built-in web search");

        part.ChooseModel("sample-plain");

        part.ReasoningOptions.Should().BeNull();
        part.StructuredOutputShown.Should().BeFalse();
    }

    [Fact]
    public async Task An_unlisted_model_id_can_be_saved_when_the_connection_has_a_catalog()
    {
        Server.Answer("PUT", $"{bot}/definition", Marshal with { Model = "sample-custom" });
        var editor = await OpenEditor();
        editor.Toggle(Model, true);
        editor.ModelPart.EntryToggleLabel.Should().Be("Enter a model ID");

        editor.ModelPart.ToggleModelEntry();
        editor.ModelPart.ModelChosenFromList.Should().BeFalse();
        ModelSectionViewModel.ModelNameLabel.Should().Be("Model name");
        editor.ModelPart.ModelFieldHint.Should().Be("Enter the model ID your provider expects.");
        editor.ModelPart.ChooseModel("sample-custom");
        await editor.Save();

        Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which.Should().Contain("\"model\":\"sample-custom\"");
    }

    [Fact]
    public async Task A_model_without_tool_calling_hides_the_tool_controls_and_says_why()
    {
        var editor = await OpenEditor();
        editor.Toggle(Model, true);
        editor.ModelPart.ChooseModel("sample-plain");
        editor.Toggle(Model, false);

        $"{Tools}, {editor.Summary(Tools)}".Should().Be("Tools, This model cannot use tools");
        editor.Toggle(Tools, true);

        editor.ToolsPart.ModelCallsTools.Should().BeFalse("the controls are replaced by the reason");
        ToolsSectionViewModel.CannotCallTools.Should().Contain("cannot call tools").And.Contain("Model section");
    }

    [Fact]
    public async Task Each_tool_is_off_ask_or_allow_and_a_change_is_sent_at_once_and_announced()
    {
        Server.Answer("PUT", $"{bot}/tools/{toolId}", TimeTool("ask", "allow", null));
        var editor = await OpenEditor();
        editor.Toggle(Tools, true);
        var tool = editor.Tools!.Single();

        editor.ToolsPart.GrantLabel(tool).Should().Be("Current time for Marshal");
        ToolsSectionViewModel.OptionsFor(tool).Select(option => option.Label).Should().Equal("Off", "Ask", "Allow");
        await editor.ToolsPart.Set(tool, "ask");

        editor.ToolsPart.Announcement.Should().Be("Current time is now ask.");
        Server.Sent("PUT", $"{bot}/tools/{toolId}").Should().ContainSingle().Which.Should().Contain("\"grant\":\"ask\"");
        editor.Tools!.Single().Grant.Should().Be("ask");
        editor.ToolsPart.Busy.Should().BeNull();
    }

    [Fact]
    public async Task A_tool_the_parent_lacks_cannot_be_turned_on_and_says_whose_it_is()
    {
        Server.Answer("GET", $"{bot}/tools", (BotToolEntry[])[TimeTool("off", "off", "Sample parent")]);
        var editor = await OpenEditor();
        editor.Toggle(Tools, true);
        var tool = editor.Tools!.Single();

        ToolsSectionViewModel.Limit(tool).Should().Be("Sample parent does not have this.");
        ToolsSectionViewModel.OptionsFor(tool).Select(option => option.Disabled).Should().Equal(false, true, true);
    }

    [Fact]
    public async Task A_connection_is_a_group_of_its_tools_and_a_changed_tool_shows_what_was_reviewed_and_can_be_accepted()
    {
        var connection = Guid.Parse("0196f1a0-0000-7000-8000-000000000008");
        var changed = new BotToolEntry(Guid.Parse("0196f1a0-0000-7000-8000-000000000009"), "lookup", "Look up", "Sample new words", "connection", connection, "Sample server", "write", "allow", "allow", null, "available")
        {
            NeedsReview = true,
            Reviewed = new ToolDefinitionSnapshot("Look up", "Sample old words", "{}"),
        };
        var gone = changed with { ToolId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a"), Name = "gone", Title = "Gone", NeedsReview = false, Reviewed = null, State = "missing" };
        Server.Answer("GET", $"{space}/tool-connections", (ToolConnectionSummary[])[
            new ToolConnectionSummary(connection, "Sample server", "sample", "https://tools.example.test/mcp", "none", "untrusted", "automatic", "connected", null, null, null),
            new ToolConnectionSummary(Guid.Parse("0196f1a0-0000-7000-8000-00000000000b"), "Quiet server", "quiet", "https://quiet.example.test/mcp", "none", "untrusted", "automatic", "failed", null, null, "Sample failure."),
        ]);
        Server.Answer("GET", $"{bot}/tools", (BotToolEntry[])[TimeTool("allow", "allow", null), changed, gone]);
        Server.Answer("POST", $"{space}/tool-connections/{connection}/tools/{changed.ToolId}/review", new ConnectionToolSummary(changed.ToolId, connection, "lookup", "sample_lookup", "Look up", "Sample new words", "write", "available", DateTimeOffset.UnixEpoch));
        var editor = await OpenEditor();
        editor.Summary(Tools).Should().Be("1 allowed · 1 ask first · 1 to review");
        editor.Toggle(Tools, true);
        var part = editor.ToolsPart;

        part.Groups.Select(group => $"{group.Name} {ToolsSectionViewModel.Count(group)}").Should().Equal(
            "Built in 1 of 1 on",
            "Sample server 2 of 2 on · 1 to review",
            "Quiet server 0 of 0 on");
        part.Groups.Where(group => group.Tools.Count == 0).Select(ToolsSectionViewModel.Empty).Should().Equal("Quiet server has listed no tools. Sample failure.");
        ToolsSectionViewModel.NeedsReview.Should().Be("Changed, needs review");
        part.ReviewWords.Should().Contain("Marshal asks before each use");
        var reviewed = editor.Tools!.Single(tool => tool.NeedsReview);
        string[] labels = [ToolsSectionViewModel.ReviewedWords(reviewed, reviewed.Reviewed!), ToolsSectionViewModel.NowWords(reviewed)];
        labels
            .Should().Equal("Look up\nSample old words", "Look up\nSample new words");
        var sampleServer = part.Groups[1];
        sampleServer.Tools.Where(tool => tool.State is "missing").Select(_ => ToolsSectionViewModel.NoLongerListed(sampleServer))
            .Should().Equal("Sample server no longer lists this tool, so it cannot be used.");

        Server.Answer("GET", $"{bot}/tools", (BotToolEntry[])[TimeTool("allow", "allow", null), changed with { NeedsReview = false, Reviewed = null }, gone]);
        await part.AcceptChange(reviewed, connection);

        editor.Tools!.Should().NotContain(tool => tool.NeedsReview);
        Server.Sent("POST", $"{space}/tool-connections/{connection}/tools/{changed.ToolId}/review").Should().ContainSingle();
        part.Announcement.Should().Be("The change to Look up is accepted.");
    }

    [Fact]
    public async Task A_working_tool_connection_shows_no_stale_error_in_the_tools_section()
    {
        var workingId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000c");
        var failedId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000d");
        Server.Answer("GET", $"{space}/tool-connections", (ToolConnectionSummary[])[
            new(workingId, "Working server", "working", "https://working.example.test/mcp", "none", "untrusted", "automatic", "ready", null, null, "Sample old failure."),
            new(failedId, "Failed server", "failed", "https://failed.example.test/mcp", "none", "untrusted", "automatic", "failed", null, null, "Sample current failure.")]);
        var editor = await OpenEditor();
        editor.Toggle(Tools, true);

        var groups = editor.ToolsPart.Groups;

        groups.Single(group => group.ConnectionId == workingId).Problem.Should().BeNull("the last error is stale once the connection works");
        groups.Single(group => group.ConnectionId == failedId).Problem.Should().Be("Sample current failure.");
    }

    [Fact]
    public async Task A_grant_the_server_refuses_is_said_in_its_words_and_the_control_goes_back()
    {
        Server.Refuse("PUT", $"{bot}/tools/{toolId}", HttpStatusCode.Conflict, "Sample parent has this tool set to ask. Raise it on Sample parent first.");
        var editor = await OpenEditor();
        editor.Toggle(Tools, true);

        await editor.ToolsPart.Set(editor.Tools!.Single(), "off");

        editor.ToolsPart.Problem.Should().Contain("Raise it on Sample parent first.");
        editor.Tools!.Single().Grant.Should().Be("allow", "the grant that still holds is shown");
        editor.ToolsPart.Version.Should().Be(1, "the control is drawn again");
    }

    #endregion

    #region Limits

    [Fact]
    public async Task The_limits_section_names_each_limit_says_what_the_space_allows_and_saves_the_bots_own()
    {
        Server.Answer("PUT", $"{bot}/definition", Marshal with { RunLimits = new RunLimitsBody(12, null, 0) });
        var editor = await OpenEditor();
        editor.Toggle(Limits, true);
        var options = editor.RunLimitOptions;

        string[] labels = [LimitsSectionFields.StepsLabel, LimitsSectionFields.TokensLabel, LimitsSectionFields.DelegationsLabel];
        labels.Should().Equal("Model calls", "Tokens", "Handoffs");
        int?[] limits = [options?.MaxSteps, options?.MaxTokens, options?.MaxDelegations];
        LimitsSectionFields.StepsPlaceholder(options).Should().Be("No limit");
        limits.Skip(1).Select(LimitsSectionFields.Placeholder).Should().Equal("5,000,000", "49");
        LimitsSectionFields.Caption.Should().Be("These limits stop a bot before it uses too much for one piece of work.");
        LimitsSectionFields.FallbackCaption.Should().Be("Leave a limit empty to use the space's.");
        LimitsSectionFields.StepsHint(options).Should().EndWith("No limit.");
        LimitsSectionFields.StepsHint(new BotRunLimitOptions(7, 5_000_000, 49)).Should().EndWith("This space allows up to 7.");
        LimitsSectionFields.StepsPlaceholder(new BotRunLimitOptions(7, 5_000_000, 49)).Should().Be("7");
        LimitsSectionFields.StepsPlaceholder(null).Should().BeEmpty();
        LimitsSectionFields.StepsHint(null).Should().Be("Each time the bot asks its model what to do next.");
        LimitsSectionFields.TokensHint(options).Should().EndWith("This space allows up to 5,000,000.");
        LimitsSectionFields.DelegationsHint(options).Should().Contain("0 keeps all work with this bot. This space allows up to 49.");
        LimitsSectionFields.Placeholder(BotRunLimitOptions.NoLimit).Should().Be("No limit");
        LimitsSectionFields.StepsHint(new BotRunLimitOptions(null, 1, 0)).Should().EndWith("No limit.");
        LimitsSectionFields.DelegationsHint(new BotRunLimitOptions(null, 5_000_000, BotRunLimitOptions.NoLimit))
            .Should().EndWith("This space sets no limit.");

        editor.Edit(draft => draft.MaxSteps = "12");
        editor.Edit(draft => draft.MaxDelegations = "0");
        await editor.Save();

        Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which.Should().Contain("\"runLimits\":{\"maxSteps\":12,\"maxTokens\":null,\"maxDelegations\":0}");
        editor.Summary(Limits).Should().Be("12 model calls · 5,000,000 tokens · 0 handoffs");
    }

    [Fact]
    public void The_limits_section_renders_no_info_box_and_keeps_the_fallback_rule_in_a_caption()
    {
        var source = Repository.NativeFiles(".cs").Single(file => file.Path == "Pages/Bots/Parts/LimitsSectionView.cs").Text;

        source.Should().NotContain("StatusNote", "the Limits section uses a compact caption line instead of an info box");
        source.Should().Contain("LimitsSectionFields.Caption").And.Contain("LimitsSectionFields.FallbackCaption").And.Contain("\"DvTextCaption\"");
        LimitsSectionFields.Caption.Should().NotContain("runs away");
        LimitsSectionFields.FallbackCaption.Should().Be("Leave a limit empty to use the space's.");
    }

    [Fact]
    public async Task A_limit_that_is_not_a_whole_number_is_said_before_anything_is_sent_and_the_servers_refusal_is_shown()
    {
        Server.Refuse("PUT", $"{bot}/definition", HttpStatusCode.BadRequest, "The limit on model calls is a whole number above zero.");
        var editor = await OpenEditor();
        editor.Toggle(Limits, true);

        editor.Edit(draft => draft.MaxTokens = "lots");
        await editor.Save();

        editor.SaveProblem.Should().Contain("The limit on tokens is a whole number.");
        Server.Sent("PUT", $"{bot}/definition").Should().BeEmpty();

        editor.Edit(draft => draft.MaxTokens = string.Empty);
        editor.Edit(draft => draft.MaxSteps = "500");
        await editor.Save();

        editor.SaveProblem.Should().Contain("whole number above zero");
    }

    #endregion

    #region Saving

    [Fact]
    public async Task An_edit_shows_the_save_bar_and_saving_sends_the_whole_definition()
    {
        Server.Answer("PUT", $"{bot}/definition", Marshal with { Name = "Scout" });
        var editor = await OpenEditor();

        editor.Rename("Scout");

        editor.ShowsSaveBar.Should().BeTrue();
        editor.SaveWords.Should().Be("Changes not saved yet");
        editor.ShownName.Should().Be("Scout", "the character follows the name as it is typed");
        await editor.Save();

        editor.ShowsSaveBar.Should().BeFalse();
        var sent = Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Subject;
        sent.Should().Contain("\"name\":\"Scout\"").And.Contain("\"handle\":\"scout\"", "the handle follows the name until someone chooses it").And.Contain("\"sandboxPolicy\":\"none\"").And.Contain("\"modelSettings\"");
        editor.Heading.Should().Be("Edit Scout");
        editor.SaveWords.Should().Be("Saved");
        Toasts.Toasts.Select(toast => toast.Message).Should().Contain("Settings saved for Scout.");
    }

    [Fact]
    public async Task A_handle_the_person_typed_stays_through_a_save_and_a_later_rename_and_a_followed_one_says_so()
    {
        Server.Answer("PUT", $"{bot}/definition", Marshal with { Name = "Scout", Handle = "scout" });
        var editor = await OpenEditor();

        editor.Rename("Scout");
        await editor.Save();

        Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which
            .Should().Contain("\"handleFollowsName\":true", "the handle is the one the name made, so the server may number it");

        Server.Answer("PUT", $"{bot}/definition", Marshal with { Name = "Scout", Handle = "scout-2", HandleChosen = true });
        editor.Edit(draft => draft.Handle = "scout-2");
        await editor.Save();
        editor.Rename("Author");
        editor.Draft!.Handle.Should().Be("scout-2", "a handle spelled like a numbered one, set by the person, is theirs after a save too");
        await editor.Save();

        var sent = Server.Sent("PUT", $"{bot}/definition").ToList();
        sent[1].Should().Contain("\"handleFollowsName\":false", "a handle the person chose is never renumbered");
        sent[2].Should().Contain("\"handle\":\"scout-2\"").And.Contain("\"handleFollowsName\":false");
    }

    [Fact]
    public async Task A_refused_save_says_what_the_server_said_and_keeps_what_was_typed()
    {
        Server.Refuse("PUT", $"{bot}/definition", HttpStatusCode.BadRequest, "Give the bot a name of up to 200 characters.");
        var editor = await OpenEditor();

        editor.Rename(" ");
        await editor.Save();

        editor.SaveProblem.Should().Contain("Give the bot a name");
        editor.Draft!.Name.Should().Be(" ");
    }

    [Fact]
    public async Task The_save_bar_marks_a_problem_so_a_phone_gives_it_a_row_of_its_own()
    {
        Server.Refuse("PUT", $"{bot}/definition", HttpStatusCode.BadRequest, "Give the bot a name of up to 200 characters.");
        var editor = await OpenEditor();

        editor.Rename("Scout");
        editor.SaveProblem.Should().BeNull();
        await editor.Save();

        editor.SaveProblem.Should().Contain("Give the bot a name");
        editor.ShowsSaveBar.Should().BeTrue();
        string[] labels = [BotEditorViewModel.DiscardLabel, editor.SaveLabel];
        labels.Should().Equal("Discard", "Save changes");
    }

    [Fact]
    public async Task A_number_that_is_not_a_number_is_said_before_anything_is_sent()
    {
        var editor = await OpenEditor();
        editor.Toggle(Team, true);

        editor.Edit(draft => draft.MaxChildren = "many");
        await editor.Save();

        editor.SaveProblem.Should().Contain("The number of bots it may build is a whole number.");
        Server.Sent("PUT", $"{bot}/definition").Should().BeEmpty();
    }

    [Fact]
    public async Task Leaving_with_changes_not_saved_asks_first_and_goes_where_the_person_was_going()
    {
        var editor = await OpenEditor();
        editor.Rename("Scout");

        Navigator.NavigateTo(Addresses.Models);

        Navigator.Current.Should().Be(Addresses.Home, "the editor stays until the person says");
        editor.Leaving.Should().BeTrue();
        editor.LeaveCause.Should().Be("The changes to Marshal are not saved yet. Leaving drops them.");

        editor.Leave();

        Navigator.Current.Should().Be(Addresses.Models);
        editor.Close();
        Navigator.Leaving.Should().BeNull();
    }

    [Fact]
    public async Task A_step_back_with_changes_not_saved_asks_first_and_then_goes_back()
    {
        Navigator.NavigateTo(Addresses.Bots);
        Navigator.NavigateTo(Addresses.Bot(botId));
        var editor = await OpenEditor();
        editor.Rename("Scout");

        Navigator.StepBack().Should().Be(BackStep.Held);

        Navigator.Current.Should().Be(Addresses.Bot(botId));
        editor.Leaving.Should().BeTrue();

        editor.Leave();

        Navigator.Current.Should().Be(Addresses.Bots);
        Navigator.CanGoForward.Should().BeTrue("the person went back, so the next step back does not open the editor again");
    }

    [Fact]
    public async Task Browser_back_with_changes_not_saved_stays_and_asks_first()
    {
        Navigator.NavigateTo(Addresses.Bots);
        Navigator.NavigateTo(Addresses.Bot(botId));
        var editor = await OpenEditor();
        editor.Rename("Scout");

        Navigator.Arrive(Addresses.Bots).Should().BeFalse("the browser's address is put back");

        Navigator.Current.Should().Be(Addresses.Bot(botId));
        editor.Leaving.Should().BeTrue();
        editor.Draft!.Name.Should().Be("Scout");
    }

    [Fact]
    public async Task Closing_the_app_asks_first_only_while_changes_are_not_saved()
    {
        var editor = await OpenEditor();
        Navigator.HoldsWork!().Should().BeFalse();

        editor.Rename("Scout");
        Navigator.HoldsWork!().Should().BeTrue();

        Navigator.NavigateTo(Addresses.Models);
        editor.Leave();
        Navigator.HoldsWork!().Should().BeFalse("the person chose to drop the changes");

        editor.Close();
        Navigator.HoldsWork.Should().BeNull();
    }

    [Fact]
    public async Task Leaving_with_nothing_to_save_asks_nothing()
    {
        var editor = await OpenEditor();

        Navigator.NavigateTo(Addresses.Models);

        Navigator.Current.Should().Be(Addresses.Models);
        editor.Leaving.Should().BeFalse();
    }

    #endregion

    #region Making A Bot

    [Fact]
    public async Task New_bot_opens_an_unsaved_draft_on_the_connection_and_stores_nothing()
    {
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(null);

        editor.IsNew.Should().BeTrue();
        editor.State.Should().Be(BotEditorState.Shown);
        editor.Heading.Should().Be("New bot");
        editor.Draft!.Name.Should().Be("New bot");
        editor.Draft.Handle.Should().Be("new-bot");
        editor.Draft.ModelConnectionId.Should().Be(connectionId);
        editor.Draft.Model.Should().Be("sample-large");
        editor.Summary(Limits).Should().Be("No limit · 5,000,000 tokens · 49 handoffs");
        editor.SaveWords.Should().Be("Not made until you save");
        editor.SaveLabel.Should().Be("Make bot");
        editor.CanSave.Should().BeTrue();
        BotEditorViewModel.Sections.Where(editor.ShowsSection).Should().Equal(Model, BotEditorViewModel.MemorySection, Team, Limits);
        Server.Requests.Should().NotContain(request => request.Method == "POST" || request.Method == "PUT", "nothing is stored until the person saves");
    }

    [Fact]
    public async Task A_new_bot_prefers_a_working_connection_over_one_that_just_failed()
    {
        var failedId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000c");
        var workingId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000d");
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[
            new(failedId, "Just failed", "venice", null, true, "failed", "sample-failed", [], null, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new(workingId, "Working", "venice", null, true, "ready", "sample-working", [], null, DateTimeOffset.UtcNow.AddDays(-1))]);
        Server.Answer("GET", $"{space}/model-connections/{failedId}/models", (ModelCatalogResponse[])[CatalogModel("sample-failed", toolCalling: true)]);
        Server.Answer("GET", $"{space}/model-connections/{workingId}/models", (ModelCatalogResponse[])[CatalogModel("sample-working", toolCalling: true)]);
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(null);

        editor.Draft!.ModelConnectionId.Should().Be(workingId, "a connection whose test just failed is not chosen over one that works");
        editor.Draft.Model.Should().Be("sample-working");
    }

    [Fact]
    public async Task A_new_bot_prefers_an_untested_connection_over_a_failed_one()
    {
        var failedId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000c");
        var pendingId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000e");
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[
            new(failedId, "Just failed", "venice", null, true, "failed", "sample-failed", [], null, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new(pendingId, "Not tested yet", "venice", null, true, "pending", "sample-pending", [], null, null)]);
        Server.Answer("GET", $"{space}/model-connections/{failedId}/models", (ModelCatalogResponse[])[CatalogModel("sample-failed", toolCalling: true)]);
        Server.Answer("GET", $"{space}/model-connections/{pendingId}/models", (ModelCatalogResponse[])[CatalogModel("sample-pending", toolCalling: true)]);
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(null);

        editor.Draft!.ModelConnectionId.Should().Be(pendingId, "an untested connection outranks one that failed");
        editor.Draft.Model.Should().Be("sample-pending");
    }

    [Fact]
    public async Task A_connection_with_no_default_model_gives_the_draft_the_model_a_bot_already_uses_on_it()
    {
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection with { DefaultModel = null }]);
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[BotSummaryOf(botId, "Marshal") with { Model = "sample-plain" }]);
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(null);

        editor.Draft!.Model.Should().Be("sample-plain");
    }

    [Fact]
    public async Task Leaving_an_unmade_bot_asks_first_and_creates_nothing()
    {
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);

        Navigator.NavigateTo(Addresses.Bots);

        Navigator.Current.Should().Be(Addresses.Home, "the editor stays until the person says");
        editor.Leaving.Should().BeTrue();
        editor.LeaveCause.Should().Be("New bot is not made yet. Leaving drops it.");
        Navigator.HoldsWork!().Should().BeTrue();

        editor.Leave();

        Navigator.Current.Should().Be(Addresses.Bots);
        Server.Requests.Should().NotContain(request => request.Method == "POST" || request.Method == "PUT", "leaving a draft stores no bot");
    }

    [Fact]
    public async Task Saving_an_unmade_bot_makes_it_once_with_the_name_model_and_handle_the_server_numbered()
    {
        Server.Answer("POST", $"{space}/bots", Made("writer-2"), HttpStatusCode.Created);
        Server.Answer("PUT", $"{space}/bots/{madeId}/definition", Marshal with { Id = madeId, Name = "Writer", Handle = "writer-2" });
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");
        editor.Edit(draft => draft.DescriptionForBots = "Writes things.");
        editor.Draft!.Handle.Should().Be("writer", "the handle follows the name until someone chooses it");

        await editor.Save();
        await editor.Save();

        Navigator.Current.Should().Be(Addresses.Bot(madeId));
        var sent = Server.Sent("POST", $"{space}/bots").Should().ContainSingle("the bot is made once").Subject;
        sent.Should().Contain("\"name\":\"Writer\"").And.Contain("\"model\":\"sample-large\"").And.Contain($"\"modelConnectionId\":\"{connectionId}\"");
        var update = Server.Sent("PUT", $"{space}/bots/{madeId}/definition").Should().ContainSingle().Subject;
        update.Should().Contain("\"handle\":\"writer-2\"", "the handle the server made from the name is kept").And.Contain("Writes things.");
    }

    [Fact]
    public async Task A_handle_the_person_chose_is_kept_when_the_bot_is_made()
    {
        Server.Answer("POST", $"{space}/bots", Made("writer"), HttpStatusCode.Created);
        Server.Answer("PUT", $"{space}/bots/{madeId}/definition", Marshal with { Id = madeId, Handle = "my-writer" });
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");
        editor.Edit(draft => draft.Handle = "my-writer");

        await editor.Save();

        Server.Sent("PUT", $"{space}/bots/{madeId}/definition").Should().ContainSingle().Which.Should().Contain("\"handle\":\"my-writer\"");
    }

    [Fact]
    public async Task New_bot_under_a_bot_keeps_its_parent_through_the_draft()
    {
        var parent = BotSummaryOf(botId, "Marshal");
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[parent]);
        Server.Answer("POST", $"{space}/bots", Made(), HttpStatusCode.Created);
        Server.Answer("PUT", $"{space}/bots/{madeId}/definition", Marshal with { Id = madeId });
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(botId);
        await editor.Save();

        Server.Sent("POST", $"{space}/bots").Should().ContainSingle().Which.Should().Contain($"\"parentBotId\":\"{botId}\"");
    }

    [Fact]
    public async Task A_parent_that_is_not_in_the_space_says_the_bot_is_not_here()
    {
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(Guid.Parse("0196f1a0-0000-7000-8000-0000000000ff"));

        editor.State.Should().Be(BotEditorState.Missing);
    }

    [Fact]
    public async Task A_refusal_to_make_a_bot_is_said_in_the_servers_words_and_the_draft_stays()
    {
        Server.Refuse("POST", $"{space}/bots", HttpStatusCode.Conflict, "This space holds at most 1 bot.");
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");

        await editor.Save();

        editor.SaveProblem.Should().Be("This space holds at most 1 bot.");
        editor.IsNew.Should().BeTrue();
        editor.Draft!.Name.Should().Be("Writer");
        Navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task A_refused_second_step_keeps_the_draft_on_screen_and_the_next_save_sets_it_on_the_bot_that_was_made()
    {
        Server.Answer("POST", $"{space}/bots", Made("writer"), HttpStatusCode.Created);
        Server.Refuse("PUT", $"{space}/bots/{madeId}/definition", HttpStatusCode.BadRequest, "Another bot already uses the handle my-writer. Choose a different one.");
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");
        editor.Edit(draft => draft.DescriptionForBots = "Writes things.");
        editor.Edit(draft => draft.Instructions = "Write briefly.");
        editor.Edit(draft => draft.Handle = "my-writer");

        await editor.Save();

        Navigator.Current.Should().Be(Addresses.Home, "the editor stays, so nothing typed is lost");
        editor.IsNew.Should().BeTrue();
        editor.IsMade.Should().BeTrue();
        editor.State.Should().Be(BotEditorState.Shown);
        editor.SaveProblem.Should().Be($"{BotEditorViewModel.MadeButNotSet} Another bot already uses the handle my-writer. Choose a different one.");
        (editor.Draft!.Name, editor.Draft.DescriptionForBots, editor.Draft.Instructions, editor.Draft.Handle)
            .Should().Be(("Writer", "Writes things.", "Write briefly.", "my-writer"));
        editor.SaveLabel.Should().Be("Save changes");
        editor.SaveWords.Should().Be("Made, but its settings are not saved yet");
        editor.LeaveCause.Should().Be("Writer was made, but its settings are not saved yet. Leaving drops them and keeps the bot as it was made.");
        Server.Sent("POST", $"{space}/bots").Should().ContainSingle();

        // The person corrects the handle and saves again: the bot that was made is updated, and no second one is made.
        Server.Answer("PUT", $"{space}/bots/{madeId}/definition", Marshal with { Id = madeId, Name = "Writer", Handle = "writer-two" });
        editor.Edit(draft => draft.Handle = "writer-two");
        await editor.Save();

        Navigator.Current.Should().Be(Addresses.Bot(madeId));
        Server.Sent("POST", $"{space}/bots").Should().ContainSingle("the bot is made once");
        var updates = Server.Sent("PUT", $"{space}/bots/{madeId}/definition").ToList();
        updates.Should().HaveCount(2);
        updates[1].Should().Contain("\"handle\":\"writer-two\"").And.Contain("Writes things.").And.Contain("Write briefly.").And.Contain("\"handleFollowsName\":false");
    }

    [Fact]
    public async Task A_second_step_that_fails_for_any_reason_leaves_the_draft_and_retries_without_making_another_bot()
    {
        Server.Answer("POST", $"{space}/bots", Made(), HttpStatusCode.Created);
        Server.Refuse("PUT", $"{space}/bots/{madeId}/definition", HttpStatusCode.InternalServerError, "The server could not save this.");
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Edit(draft => draft.Instructions = "Keep what I typed.");

        await editor.Save();
        await editor.Save();

        Navigator.Current.Should().Be(Addresses.Home);
        editor.Draft!.Instructions.Should().Be("Keep what I typed.");
        editor.SaveProblem.Should().StartWith(BotEditorViewModel.MadeButNotSet);
        Server.Sent("POST", $"{space}/bots").Should().ContainSingle("a retry never makes a second bot");
        Server.Sent("PUT", $"{space}/bots/{madeId}/definition").Should().HaveCount(2);
    }

    [Fact]
    public async Task A_create_whose_answer_was_lost_is_sent_again_with_the_same_id_after_the_draft_was_edited()
    {
        var committed = Made("writer") with { Name = "Writer", Instructions = "Write briefly." };
        Server.Fail("POST", $"{space}/bots", new HttpRequestException("The connection dropped after the server committed."));
        Server.Answer("PUT", $"{space}/bots/{madeId}/definition", Marshal with { Id = madeId, Name = "Writer the second", Handle = "writer" });
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");
        editor.Edit(draft => draft.Instructions = "Write briefly.");

        await editor.Save();

        editor.SaveProblem.Should().NotBeNullOrWhiteSpace("the person is told the save failed");
        editor.IsMade.Should().BeFalse("the answer that carried the id of the bot was lost");

        // The person edits the draft before trying again. Neither the name nor the instructions matter: the retry
        // carries the id chosen for this draft, so the server returns the bot it made.
        editor.Rename("Writer the second");
        editor.Edit(draft => draft.Instructions = "Write at length.");
        Server.Answer("POST", $"{space}/bots", committed, HttpStatusCode.Created);
        await editor.Save();

        var created = Server.Sent("POST", $"{space}/bots").Select(RequestId).ToList();
        created.Should().HaveCount(2).And.OnlyContain(id => id != null);
        created[1].Should().Be(created[0], "a create is replayed with the id chosen for the draft, whatever was edited since");
        Navigator.Current.Should().Be(Addresses.Bot(madeId));
        Server.Sent("PUT", $"{space}/bots/{madeId}/definition").Should().ContainSingle().Which
            .Should().Contain("Writer the second").And.Contain("Write at length.", "the edited draft is what is set on the bot that was made");
    }

    [Fact]
    public async Task A_bot_of_the_same_name_that_someone_else_made_is_never_taken_for_the_one_being_made()
    {
        var another = Made("writer") with { Id = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1"), Name = "Writer", Instructions = "Write briefly." };
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[another]);
        Server.Fail("POST", $"{space}/bots", new HttpRequestException("The connection dropped before the server saw it."));
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");
        editor.Edit(draft => draft.Instructions = "Write briefly.");
        await editor.Save();

        Server.Answer("POST", $"{space}/bots", Made("writer-2"), HttpStatusCode.Created);
        Server.Answer("PUT", $"{space}/bots/{madeId}/definition", Marshal with { Id = madeId, Name = "Writer", Handle = "writer-2" });
        await editor.Save();

        Navigator.Current.Should().Be(Addresses.Bot(madeId), "the bot the server made for this draft, not the other person's");
        Server.Sent("PUT", $"{space}/bots/{another.Id}/definition").Should().BeEmpty("the other person's bot is never written to");
        Server.Sent("POST", $"{space}/bots").Should().HaveCount(2);
    }

    [Fact]
    public async Task A_create_the_server_refused_made_nothing_so_the_next_try_chooses_another_id()
    {
        Server.Refuse("POST", $"{space}/bots", HttpStatusCode.BadRequest, "That bot id is already used. Try again.");
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);

        await editor.Save();
        await editor.Save();

        var created = Server.Sent("POST", $"{space}/bots").Select(RequestId).ToList();
        created.Should().HaveCount(2);
        (created[1] == created[0]).Should().BeFalse("a refusal is an answer: the id is not spoken for, so the next try has its own");
        editor.SaveProblem.Should().Be("That bot id is already used. Try again.");
    }

    [Fact]
    public async Task The_request_id_chosen_for_a_draft_is_kept_for_that_draft_only()
    {
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[BotSummaryOf(botId, "Marshal")]);
        Server.Fail("POST", $"{space}/bots", new HttpRequestException("The connection dropped."));
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        await editor.Save();
        await editor.Save();

        // A different draft, under a bot, is a different bot to make: it has a request id of its own.
        await editor.OpenUnmade(botId);
        await editor.Save();

        var created = Server.Sent("POST", $"{space}/bots").Select(RequestId).ToList();
        created.Should().HaveCount(3);
        created[1].Should().Be(created[0], "the same draft keeps its request id across tries");
        (created[2] == created[0]).Should().BeFalse("the next draft chooses its own");
    }

    [Fact]
    public async Task A_handle_the_server_made_for_a_new_bot_is_not_a_choice_so_the_draft_keeps_following_the_name()
    {
        Server.Answer("POST", $"{space}/bots", Made("writer-2"), HttpStatusCode.Created);
        Server.Refuse("PUT", $"{space}/bots/{madeId}/definition", HttpStatusCode.InternalServerError, "The server could not save this.");
        var editor = Make<BotEditorViewModel>();
        await editor.OpenUnmade(null);
        editor.Rename("Writer");

        await editor.Save();

        editor.Draft!.Handle.Should().Be("writer-2", "the server numbered it");
        editor.Draft.HandleChosen.Should().BeFalse();
        Server.Sent("PUT", $"{space}/bots/{madeId}/definition").Should().ContainSingle().Which.Should().Contain("\"handleFollowsName\":true");
    }

    [Fact]
    public async Task A_member_is_told_they_cannot_make_a_bot()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(null);

        editor.State.Should().Be(BotEditorState.NotPermitted);
    }

    [Fact]
    public async Task A_space_with_no_model_says_what_is_missing_instead_of_opening_a_draft()
    {
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        var editor = Make<BotEditorViewModel>();

        await editor.OpenUnmade(null);

        editor.State.Should().Be(BotEditorState.Failed);
        BotEditorViewModel.NoModelCause.Should().Be("A bot needs a model, and this space has none yet. Connect a model first.");
        editor.LoadProblem.Should().Be(BotEditorViewModel.NoModelCause);
    }

    #endregion

    #region Sandbox

    [Fact]
    public async Task The_sandbox_section_says_no_provider_is_set_up_only_when_the_server_has_none()
    {
        var configured = await OpenEditor();
        configured.Toggle(Sandbox, true);

        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: false, OpenNetwork: true, MaxIdleMinutes: 60, Desktop: true));
        var bare = await OpenEditor();
        bare.Toggle(Sandbox, true);

        configured.SandboxPart.ProviderNote.Should().BeNull("the live region is there before any message, so a later one is announced");
        bare.SandboxPart.ProviderNote.Should().StartWith("No sandbox provider is set up on this server");
        SandboxSectionViewModel.PolicyLabel.Should().Be("Sandbox", "the choice is still offered, and saved for when a provider is added");
    }

    [Fact]
    public async Task The_sandbox_section_says_how_the_sandbox_is_now_and_leads_to_its_screen()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/sandbox", new BotSandboxStatus(true, true, "paused", DateTimeOffset.UtcNow.AddMinutes(-10.5), Desktop: true, BotWorking: false, DesktopOffered: true));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.Live.Should().Be("Paused · used 10 minutes ago");
        SandboxSectionViewModel.OpenSandbox.Should().Be("Open the sandbox");
        editor.SandboxPart.SandboxAddress.Should().Be(Addresses.BotSandbox(botId));
    }

    [Fact]
    public async Task The_sandbox_section_leaves_the_state_out_when_it_cannot_be_read()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.Live.Should().BeNull();
        editor.SandboxPart.SettingsShown.Should().BeTrue("the settings stay whatever the state");
    }

    [Fact]
    public async Task Open_network_is_not_offered_where_the_server_forbids_it_and_the_editor_says_so()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: false, MaxIdleMinutes: 15, Desktop: false));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        SandboxSectionViewModel.NetworkLabel.Should().Be("Sandbox network");
        editor.SandboxPart.Networks.Select(network => network.Label).Should().Equal("Off", "Named hosts");
        editor.SandboxPart.NoOpenNetworkShown.Should().BeTrue();
        SandboxSectionViewModel.NoOpenNetwork.Should().Be("Open internet access is not available on this server.");
        editor.SandboxPart.IdlePlaceholder.Should().Be("Up to 15");
    }

    [Fact]
    public async Task Open_network_is_offered_where_the_server_allows_it()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.Networks.Select(network => network.Label).Should().Equal("Off", "Named hosts", "Open");
        editor.SandboxPart.NoOpenNetworkShown.Should().BeFalse();
    }

    [Fact]
    public async Task The_desktop_switch_says_what_it_costs_and_shows_and_is_saved_with_the_sandbox()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("PUT", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.DesktopShown.Should().BeTrue();
        SandboxSectionViewModel.DesktopLabel.Should().Be("Desktop");
        editor.Draft!.Desktop.Should().BeFalse();
        SandboxSectionViewModel.DesktopHint.Should().Contain("costs more to run").And.Contain("can show anything on that screen").And.Contain("replaces the sandbox");
        SandboxSectionViewModel.DesktopHint.Should().NotContainEquivalentOf("e2b", "the interface names no vendor");

        editor.Edit(draft => draft.Desktop = true);
        await editor.Save();

        Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which.Should().Contain("\"desktop\":true");
    }

    [Fact]
    public async Task The_desktop_switch_is_not_offered_where_the_server_forbids_it_and_the_editor_says_so()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: false, MaxIdleMinutes: 15, Desktop: false));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.DesktopShown.Should().BeFalse();
        editor.SandboxPart.NoDesktopShown.Should().BeTrue();
        SandboxSectionViewModel.NoDesktop.Should().Be("A sandbox with a screen is not available on this server.");
    }

    [Fact]
    public async Task A_bot_saved_with_a_screen_keeps_the_switch_where_the_server_forbids_one_so_it_can_be_turned_off()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: false, MaxIdleMinutes: 15, Desktop: false));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.DesktopShown.Should().BeTrue();
        editor.Draft!.Desktop.Should().BeTrue();
        editor.SandboxPart.NoDesktopShown.Should().BeTrue();
    }

    [Fact]
    public async Task A_sandbox_shared_with_chosen_bots_lists_the_other_bots_to_choose()
    {
        var other = new BotSummary(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1"), "Sample helper", "sample-helper", string.Empty, connectionId, "sample-large", "active");
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[other]);
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "named" });
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.SharingShown.Should().BeTrue();
        editor.SandboxPart.Meaning.Should().Be("The bots you choose work in the same files as this one and can change its work.");
        editor.SandboxPart.OtherBots.Should().ContainSingle().Which.Name.Should().Be("Sample helper");
        editor.SandboxPart.Share(other.Id, true);

        editor.SandboxPart.SharesWith(other.Id).Should().BeTrue();
        editor.ShowsSaveBar.Should().BeTrue();
    }

    #endregion

    #region Routines

    [Fact]
    public async Task With_no_routines_the_section_says_what_one_is_and_offers_the_one_button()
    {
        var editor = await OpenEditor();
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        part.EmptyWords.Should().Be("A routine is a prompt Marshal is sent on its own, at the times you choose.");
        part.AddShown.Should().BeTrue();
        RoutinesSectionViewModel.AddRoutine.Should().Be("Add a routine");
        part.Schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task A_routine_is_written_from_plain_choices_saved_off_and_listed_as_its_sentence()
    {
        var made = Routine(enabled: false) with { Cron = "30 9 * * 1-5", TimeZone = "Europe/Berlin" };
        Server.Answer("POST", $"{bot}/schedules", made, HttpStatusCode.Created);
        Server.Answer("POST", $"{bot}/schedules/check", new ScheduleCheckResponse(null, []));
        var editor = await OpenEditor();
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        part.Add();
        part.TakeFocus().Should().Be(RoutineFocus.Form, "focus moves into the form that took the button's place");
        part.Prompt = "Sample prompt";
        await part.ChangeWhen(RoutineWhenFields.ChooseRhythm(part.When, "Weekdays"));
        await part.ChangeWhen(RoutineWhenFields.SetTime(part.When, "09:30")!);
        await part.ChangeTimeZone("Europe/Berlin");
        part.Reads.Should().Be("Reads as: Every weekday at 09:30, Berlin time.");
        await part.Save();

        var row = part.Row(part.Schedules.Single());
        row.Sentence.Should().Be("Every weekday at 09:30, Berlin time");
        Server.Sent("POST", $"{bot}/schedules").Should().ContainSingle().Which.Should()
            .Contain("\"cron\":\"30 9 * * 1-5\"").And.Contain("\"timeZone\":\"Europe/Berlin\"").And.Contain("\"enabled\":false");
        row.Prompt.Should().Be("Sample prompt");
        part.Announcement.Should().StartWith("Routine added, turned off");
        part.TakeFocus().Should().Be(RoutineFocus.Add);
    }

    [Fact]
    public async Task A_custom_expression_is_explained_or_refused_by_the_server_before_it_is_saved()
    {
        Server.Refuse("POST", $"{bot}/schedules", HttpStatusCode.BadRequest, "not expected");
        Server.Answer("POST", $"{bot}/schedules/check", new ScheduleCheckResponse("Schedules on this server run at most once every 60 minutes. Choose a time that repeats less often.", []));
        var editor = await OpenEditor();
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        part.Add();
        await part.ChangeWhen(RoutineWhenFields.ChooseRhythm(part.When, "Custom"));
        await part.ChangeWhen(RoutineWhenFields.SetCron(part.When, "*/20 * * * *"));

        part.Refusal.Should().Be("Schedules on this server run at most once every 60 minutes. Choose a time that repeats less often.");
        part.Reads.Should().Be("The expression */20 * * * * cannot be saved as it is.");
        Server.Sent("POST", $"{bot}/schedules/check").Last().Should().Contain("\"cron\":\"*/20 * * * *\"");
        Server.Sent("POST", $"{bot}/schedules").Should().BeEmpty("nothing is saved until the person saves");
    }

    [Fact]
    public async Task A_routine_the_server_refuses_on_save_says_why_by_the_buttons()
    {
        const string Refusal = "A space on this server holds at most 2 schedules. Remove one first.";
        Server.Refuse("POST", $"{bot}/schedules", HttpStatusCode.BadRequest, Refusal);
        Server.Answer("POST", $"{bot}/schedules/check", new ScheduleCheckResponse(null, []));
        var editor = await OpenEditor();
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        part.Add();
        part.SavedOffShown.Should().BeTrue();
        RoutinesSectionViewModel.SavedOff.Should().Be("It is saved turned off. Run it once to try it, then turn it on.");
        part.Prompt = "Sample prompt";
        await part.Save();

        part.FormProblem.Should().Be(Refusal);
        part.Refusal.Should().BeNull("a refusal that is not about the time is not said beside it");
        part.Editing.Should().BeTrue();
    }

    [Fact]
    public async Task A_routine_with_nothing_to_ask_says_so_before_anything_is_sent()
    {
        var editor = await OpenEditor();
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        part.Add();
        await part.Save();

        part.FormProblem.Should().Be("Write what to ask Marshal.");
        Server.Sent("POST", $"{bot}/schedules").Should().BeEmpty();
    }

    [Fact]
    public async Task Running_a_routine_once_links_to_its_conversation_and_its_last_failure_is_said_in_words()
    {
        var threadId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");
        var failed = Routine(enabled: true) with
        {
            LastRunAt = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero),
            LastOutcome = "failed",
            LastThreadId = threadId,
            LastReason = "The model connection could not be reached.",
        };
        Server.Answer("GET", $"{bot}/schedules", (BotScheduleResponse[])[failed]);
        Server.Answer("POST", $"{bot}/schedules/{failed.Id}/run-once", new ScheduleRunOnceResponse(threadId, Guid.CreateVersion7()), HttpStatusCode.Accepted);
        var editor = await OpenEditor("Routines, 1 routine");
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        var row = part.Row(failed);
        row.Last.Should().Contain("Failed").And.Contain(": The model connection could not be reached.");
        row.Routine.LastRun!.ConversationHref.Should().Be($"/threads/{threadId}");
        await part.AskToRunOnce(failed);

        part.Row(failed).StartedHref.Should().Be($"/threads/{threadId}");
        Server.Sent("POST", $"{bot}/schedules/{failed.Id}/run-once").Should().ContainSingle();
        Server.Sent("PUT", $"{bot}/schedules/{failed.Id}").Should().BeEmpty("running once does not change the routine");
    }

    [Fact]
    public async Task Running_another_persons_routine_once_shows_its_whole_prompt_first()
    {
        var theirs = Routine(enabled: true) with
        {
            OwnerUserId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1"),
            Prompt = "Sample prompt.\nSecond line of the sample.",
        };
        Server.Answer("GET", $"{bot}/schedules", (BotScheduleResponse[])[theirs]);
        Server.Answer("POST", $"{bot}/schedules/{theirs.Id}/run-once", new ScheduleRunOnceResponse(Guid.CreateVersion7(), Guid.CreateVersion7()), HttpStatusCode.Accepted);
        var editor = await OpenEditor("Routines, 1 routine");
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        await part.AskToRunOnce(theirs);

        Server.Sent("POST", $"{bot}/schedules/{theirs.Id}/run-once").Should().BeEmpty("someone who did not write it reads it first");
        part.ConfirmingRun.Should().BeTrue();
        part.Running!.Prompt.Should().Be("Sample prompt.\nSecond line of the sample.");
        part.RunWords.Should().Be("Every day at 09:00, UTC. Marshal is sent this, in a conversation of yours:");
        await part.RunConfirmed();

        Server.Sent("POST", $"{bot}/schedules/{theirs.Id}/run-once").Should().ContainSingle();
        var row = part.Row(theirs);
        row.StartedHref.Should().NotBeNull();
        RoutineRowViewModel.OpenStarted.Should().Be("Open the conversation it started");
        row.TakeFocusAfterRun().Should().Be(RoutineFocus.Started, "focus moves to the link that took the disabled button's place");
    }

    [Fact]
    public async Task A_refused_run_of_another_persons_routine_puts_focus_back_on_run_once_now_beside_the_reason()
    {
        const string Refusal = "Marshal is paused, so its routines do not run. Resume it first.";
        var theirs = Routine(enabled: true) with { OwnerUserId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1") };
        Server.Answer("GET", $"{bot}/schedules", (BotScheduleResponse[])[theirs]);
        Server.Refuse("POST", $"{bot}/schedules/{theirs.Id}/run-once", HttpStatusCode.Conflict, Refusal);
        var editor = await OpenEditor("Routines, 1 routine");
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        await part.AskToRunOnce(theirs);
        await part.RunConfirmed();

        var row = part.Row(theirs);
        row.Problem.Should().Be(Refusal);
        row.Busy.Should().BeFalse("Run once now is enabled again");
        row.StartedHref.Should().BeNull("there is no conversation to go to");
        row.TakeFocusAfterRun().Should().Be(RoutineFocus.RunOnce,
            "focus moves once, after the answer is in, to the only place left: Run once now, enabled again beside the refusal");
        part.Row(theirs).TakeFocusAfterRun().Should().BeNull();
    }

    [Fact]
    public async Task Removing_a_routine_asks_first()
    {
        var routine = Routine(enabled: true);
        Server.Answer("GET", $"{bot}/schedules", (BotScheduleResponse[])[routine]);
        Server.Answer("DELETE", $"{bot}/schedules/{routine.Id}", HttpStatusCode.NoContent);
        var editor = await OpenEditor("Routines, 1 routine");
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        part.AskToRemove(routine);

        Server.Sent("DELETE", $"{bot}/schedules/{routine.Id}").Should().BeEmpty("removing asks first");
        part.Confirming.Should().BeTrue();
        part.RemovingWords.Should().Be("Every day at 09:00, UTC: Sample prompt");
        await part.Remove();
        part.Schedules.Should().BeEmpty();
        Server.Sent("DELETE", $"{bot}/schedules/{routine.Id}").Should().ContainSingle();
    }

    [Fact]
    public async Task Another_admins_routine_offers_no_change_and_a_member_reads_routines_without_controls()
    {
        var theirs = Routine(enabled: true) with { OwnerUserId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1") };
        Server.Answer("GET", $"{bot}/schedules", (BotScheduleResponse[])[theirs]);
        var editor = await OpenEditor("Routines, 1 routine");
        editor.Toggle(Routines, true);
        var part = editor.RoutinesPart;

        var asOwner = part.Row(theirs);
        asOwner.CanChange.Should().BeTrue("another admin may turn it off, run it once, or remove it");
        asOwner.CanRewrite.Should().BeFalse("only the person who made it changes it");
        part.AddShown.Should().BeTrue();

        Session.Begin(Person with { Spaces = [Person.Spaces[0] with { Role = "member" }] });
        var asMember = part.Row(theirs);

        asMember.CanChange.Should().BeFalse("a member reads routines");
        part.AddShown.Should().BeFalse();
        asMember.Sentence.Should().Be("Every day at 09:00, UTC");
        editor.Schedules = [];
        part.EmptyWords.Should().Be("Marshal has no routines. Owners and admins of this space add them.");
    }

    private BotScheduleResponse Routine(bool enabled) =>
        new(Guid.Parse("0196f1a0-0000-7000-8000-000000000006"), botId, Person.UserId, "0 9 * * *", "UTC", "Sample prompt", "new_thread", enabled,
            enabled ? new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero) : null);

    #endregion

    #region Rules

    [Fact]
    public async Task With_no_rules_the_section_offers_to_write_one_and_starters_that_say_what_they_are()
    {
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        part.Own.Should().BeEmpty();
        part.Inherited.Should().BeEmpty();
        string[] labels = [RulesSectionViewModel.WriteRule];
        labels.Concat(part.Starters.Select(starter => starter.Label)).Should().Equal(
            "Write a rule",
            "Ask before anything that writes",
            "Block common forms of a card number or key",
            "Quiet hours");
        RulesSectionViewModel.StartersLegend.Should().Be("Starters. Each adds a rule you can then change.");
        part.TryShown.Should().BeFalse("there is nothing to try yet");
    }

    [Fact]
    public async Task A_starter_adds_its_rule_and_the_rule_is_announced_and_shown_as_its_sentence()
    {
        Server.Answer("POST", $"{bot}/rules", Rule("Ask before anything that writes", RuleWords.BeforeTool, "ask", new RuleConditionBody("tool_kind") { ToolKinds = ["write", "critical"] }), HttpStatusCode.Created);
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        await part.AddStarter(part.Starters[0]);

        const string Sentence = "When a tool is about to run, if the tool writes or is critical, then ask a person first.";
        var rule = part.Own.Should().ContainSingle().Subject;
        RuleWords.Sentence(rule).Should().Be(Sentence);
        part.Announcement.Should().Be($"Rule added. {Sentence}");
        Server.Sent("POST", $"{bot}/rules").Should().ContainSingle().Which.Should().Contain("\"checkpoint\":\"before_tool\"").And.Contain("\"effect\":\"ask\"").And.Contain("\"toolKinds\":[\"write\",\"critical\"]");
        RulesSectionViewModel.SwitchLabel.Should().Be("On");
        rule.Name.Should().Be("Ask before anything that writes");
        string[] labels = [RulesSectionViewModel.ChangeLabel(rule), RulesSectionViewModel.RemoveLabel(rule)];
        labels.Should().Equal(
            "Change the rule Ask before anything that writes",
            "Remove the rule Ask before anything that writes");
    }

    [Fact]
    public async Task Where_the_server_runs_no_written_patterns_the_section_says_so_and_does_not_offer_one()
    {
        Server.Answer("GET", $"{bot}/rules/options", new BotRuleOptions(false));
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        part.WrittenPatterns.Should().BeFalse("the section says so in RuleWords.WrittenPatternsOff");

        part.Add();
        part.MoveTo(RuleWords.BeforeReply);
        part.AddCondition();

        part.ConditionChoices(part.Draft!.Conditions.Single()).Select(choice => choice.Label).Should().Equal(
            "The reply contains (catches common forms)",
            "The reply holds (catches common forms)",
            "The time is between",
            "The run has used more than");
    }

    [Fact]
    public async Task A_rule_is_built_from_when_if_then_and_only_what_fits_the_moment_is_offered()
    {
        var made = Rule("Sample rule", RuleWords.BeforeReply, "redact", new RuleConditionBody("text_contains") { Entries = ["sample words"] });
        Server.Answer("POST", $"{bot}/rules", made, HttpStatusCode.Created);
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        part.Add();
        string[] labels = [part.FormLegend, RulesSectionViewModel.WhenLabel, RulesSectionViewModel.ConditionName(1), RulesSectionViewModel.IfLabel(1)];
        labels
            .Should().Equal("New rule", "When", "Condition 1", "If");
        part.Effects.Select(choice => choice.Label).Should().Equal("Ask a person first", "Do not run it", "Remove what matched", "Stop the run");

        part.MoveTo(RuleWords.BeforeReply);

        part.Draft!.Conditions.Should().BeEmpty("a condition about a tool means nothing for a reply");
        part.Effects.Select(choice => choice.Label).Should().Equal(["Do not send it", "Remove what matched", "Stop the run"], "a reply cannot wait for a person");
        part.AddConditionLabel.Should().Be("Add a condition");
        part.AddCondition();
        var condition = part.Draft.Conditions.Single();
        part.ConditionChoices(condition).Select(choice => choice.Label).Should().Equal(
            "The reply contains (catches common forms)",
            "The reply holds (catches common forms)",
            "The reply matches a pattern (catches common forms)",
            "The time is between",
            "The run has used more than");
        part.Change(() => condition.Entries = "sample words");
        condition.Kind.Should().Be("text_contains", "a person is told what a condition on text does not find, in RuleWords.NotCaught");
        part.Change(() => part.Draft.Effect = "redact");
        part.Change(() => part.Draft.Name = "Sample rule");
        $"{RulesSectionViewModel.ReadsAs} {part.Preview}".Should().Be("Reads as When the bot is about to reply, if the reply contains \"sample words\" (catches common forms), then remove what matched.");
        await part.Save();

        RuleWords.Sentence(part.Own.Single()).Should().Be("When the bot is about to reply, if the reply contains \"sample words\" (catches common forms), then remove what matched.");
        Server.Sent("POST", $"{bot}/rules").Should().ContainSingle().Which.Should()
            .Contain("\"name\":\"Sample rule\"").And.Contain("\"checkpoint\":\"before_reply\"").And.Contain("\"effect\":\"redact\"").And.Contain("\"entries\":[\"sample words\"]");
        part.Draft.Should().BeNull("the builder closes once the rule is saved");
    }

    [Fact]
    public async Task The_section_says_which_conditions_are_exact_and_which_catch_common_forms()
    {
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);

        RuleWords.Intro.Should().Be(
            "The server checks a rule every time, whatever the bot's instructions say. Conditions about the tool, the time, counts, and named inputs are exact. Conditions that look for something in text catch common forms and are not a guarantee.");
        editor.RulesPart.Loading.Should().BeFalse();
        string[] labels = [RuleWords.Intro, editor.RulesPart.TryHint, RulesSectionViewModel.StartersLegend];
        labels.Should().NotContain(text =>
            text.Contains("hard limit", StringComparison.Ordinal) || text.Contains("never leaks", StringComparison.Ordinal) || text.Contains("guarantees that", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_the_server_notes_about_a_rule_is_shown_with_it_and_read_out_when_it_is_saved()
    {
        const string Note = "No tool has that name now: sample.typo. The rule does not apply to it until one does. Check the spelling.";
        var noted = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_is") { Tools = ["sample.typo"] }) with { Notes = [Note] };
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[noted]);
        Server.Answer("PUT", $"{bot}/rules/{ruleId}", noted);
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        part.Own.Single().Notes.Should().Contain(Note);

        await part.SwitchRule(part.Own.Single(), false);

        part.Announcement.Should().EndWith(Note);
    }

    [Fact]
    public async Task A_trial_decided_by_a_pattern_condition_says_it_is_not_a_guarantee()
    {
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[Rule("Sample rule", RuleWords.BeforeReply, "block", new RuleConditionBody("text_holds") { Builtin = "card_number" })]);
        Server.Answer("POST", $"{bot}/rules/try", new TryRulesResponse("block", ruleId, "Sample rule", "the text holds a card number", null, null, ["Sample rule"]) { CatchesCommonForms = true });
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        part.TryCheckpoint = RuleWords.BeforeReply;
        await part.Try();

        part.TrialWords.Should().Be(
            "The rule \"Sample rule\" would block this: the text holds a card number. Some of the rules tried look for something in text: they catch common forms and are not a guarantee.");
    }

    [Fact]
    public async Task A_list_condition_starts_on_the_fields_a_recipient_can_be_in_and_the_address_form_is_marked_a_pattern()
    {
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        part.Add();
        var condition = part.Draft!.Conditions.Single();
        part.ConditionChoices(condition).Select(choice => choice.Label).Should().Contain(["An input is not on a list", "An email address is not on a list (catches common forms)"]);

        part.Change(() => condition.Kind = "not_in_list");

        condition.Argument.Should().Be("to, cc, bcc");
        $"{RulesSectionViewModel.ReadsAs} {part.Preview}".Should().StartWith("Reads as When a tool is about to run, if any of the inputs \"to\", \"cc\", \"bcc\" is not one of");
    }

    [Fact]
    public async Task A_rule_the_server_refuses_stays_in_the_builder_with_the_servers_reason()
    {
        Server.Refuse("POST", $"{bot}/rules", HttpStatusCode.BadRequest, "Give the rule a name.");
        var editor = await OpenEditor();
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        part.Add();

        await part.Save();

        part.Problem.Should().Contain("Give the rule a name.");
        part.Draft.Should().NotBeNull("the builder stays open");
    }

    [Fact]
    public async Task Rules_from_the_bots_above_are_listed_with_where_they_come_from_and_cannot_be_changed_here()
    {
        var parent = Rule("Sample parent rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["critical"] }) with { BotName = "Sample parent", Inherited = true };
        var own = Rule("Sample own rule", RuleWords.MessageArrives, "stop", new RuleConditionBody("text_holds") { Builtin = "key" }) with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000008"), Enabled = false };
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[parent, own]);
        var editor = await OpenEditor("Rules, 2 rules, 1 off");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        string[] labels = [RulesSectionViewModel.InheritedName, RulesSectionViewModel.OwnName];
        labels.Should().Equal("Rules from the bots above", "Rules");
        var inherited = part.Inherited.Should().ContainSingle().Subject;
        $"{RuleWords.Sentence(inherited)} {RulesSectionViewModel.InheritedLine(inherited)}".Should().Be(
            "When a tool is about to run, if the tool is critical, then do not run it. Sample parent rule · from Sample parent, changed there");
        var mine = part.Own.Should().ContainSingle().Subject;
        RuleWords.Sentence(mine).Should().Be("When a message arrives, if the message holds a key or token (catches common forms), then stop the run.");
        mine.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Trying_a_sample_asks_the_server_and_shows_its_answer_in_words()
    {
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[Rule("Sample rule", RuleWords.BeforeReply, "redact", new RuleConditionBody("text_contains") { Entries = ["sample words"] })]);
        Server.Answer("POST", $"{bot}/rules/try", new TryRulesResponse("redact", ruleId, "Sample rule", "the text contains text the rule names", "Some [removed by a rule] here.", null, ["Sample rule"]));
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        part.TryCheckpoint = RuleWords.BeforeReply;
        part.TrySampleLabel.Should().Be("Sample text");
        part.TrySample = "Some sample words here.";
        await part.Try();

        string?[] labels = [part.TrialWords, RulesSectionViewModel.WhatWouldBeLeft, part.Left];
        labels.Should().Equal(
            "The rule \"Sample rule\" would remove what matched: the text contains text the rule names.",
            "What would be left",
            "Some [removed by a rule] here.");
        Server.Sent("POST", $"{bot}/rules/try").Should().ContainSingle().Which.Should()
            .Contain("\"checkpoint\":\"before_reply\"").And.Contain("\"text\":\"Some sample words here.\"").And.NotContain("\"draft\":{");
    }

    [Fact]
    public async Task Removing_a_rule_offers_to_put_it_back()
    {
        var rule = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["critical"] });
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[rule]);
        Server.Answer("DELETE", $"{bot}/rules/{ruleId}", HttpStatusCode.NoContent);
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        using var toasts = new ToastHostViewModel(Toasts);

        await part.Remove(rule);

        Server.Addresses.Should().ContainSingle(address => address.StartsWith("DELETE ", StringComparison.Ordinal)).Which
            .Should().Be($"DELETE {bot}/rules/{ruleId}");
        editor.Removing.Should().BeFalse("a removal asks nothing first");
        part.Own.Should().BeEmpty();
        part.Announcement.Should().Be("Rule removed: Sample rule.");
        part.TakeFocus().Should().Be(RuleFocus.Add, "the row is gone, so focus goes to Add a rule");
        var toast = toasts.Quiet.Should().ContainSingle().Subject;
        (toast.Message, toast.Kind).Should().Be(("Rule removed: Sample rule.", ToastKind.Info));
        (toast.Action!.Label, toast.Action.Name, toast.Action.Until).Should().Be(("Undo", "Undo removing Sample rule", null));
    }

    [Fact]
    public async Task Undo_creates_the_rule_again_with_everything_it_had()
    {
        var first = Rule("First", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["read"] }) with
        {
            Id = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1"),
        };
        var removed = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["write", "critical"] }) with
        {
            Enabled = false,
            Position = 1,
        };
        var last = Rule("Last", RuleWords.BeforeReply, "redact", new RuleConditionBody("text_contains") { Entries = ["sample"] }) with
        {
            Id = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b2"),
            Position = 2,
        };
        var restored = removed with { Id = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b3") };
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[first, removed, last]);
        Server.Answer("DELETE", $"{bot}/rules/{ruleId}", HttpStatusCode.NoContent);
        Server.Answer("POST", $"{bot}/rules", restored, HttpStatusCode.Created);
        var editor = await OpenEditor("Rules, 3 rules, 1 off");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        using var toasts = new ToastHostViewModel(Toasts);

        await part.Remove(removed);
        await toasts.Act(toasts.Quiet.Single());

        var post = Server.Sent("POST", $"{bot}/rules").Should().ContainSingle().Subject;
        using var sent = JsonDocument.Parse(post);
        var body = sent.RootElement;
        body.GetProperty("name").GetString().Should().Be("Sample rule");
        body.GetProperty("enabled").GetBoolean().Should().BeFalse();
        body.GetProperty("checkpoint").GetString().Should().Be("before_tool");
        body.GetProperty("effect").GetString().Should().Be("block");
        body.GetProperty("position").GetInt32().Should().Be(1);
        body.GetProperty("conditions")[0].GetProperty("toolKinds").EnumerateArray().Select(kind => kind.GetString())
            .Should().Equal("write", "critical");
        string[] names = [.. part.Own.Select(rule => rule.Name)];
        names.Should().Equal("First", "Sample rule", "Last");
        part.Announcement.Should().Be("Rule put back: Sample rule.");
        Toasts.Toasts.Should().BeEmpty("the put back is said in the section, not in a toast");
    }

    [Fact]
    public async Task A_refused_removal_offers_no_undo()
    {
        var rule = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["critical"] });
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[rule]);
        Server.Refuse("DELETE", $"{bot}/rules/{ruleId}", HttpStatusCode.BadRequest, "The rule is in use.");
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;

        await part.Remove(rule);

        part.Problem.Should().Be("The rule is in use.");
        part.Own.Should().ContainSingle().Which.Id.Should().Be(ruleId);
        Toasts.Toasts.Should().BeEmpty("a rule that was not removed offers nothing to put back");
        editor.Removing.Should().BeFalse();
    }

    [Fact]
    public async Task An_undo_the_server_refuses_says_why_and_can_be_tried_again()
    {
        const string Reason = "A bot has at most 50 rules of its own. Remove one first.";
        var rule = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["critical"] });
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[rule]);
        Server.Answer("DELETE", $"{bot}/rules/{ruleId}", HttpStatusCode.NoContent);
        Server.Refuse("POST", $"{bot}/rules", HttpStatusCode.BadRequest, Reason);
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        using var toasts = new ToastHostViewModel(Toasts);

        await part.Remove(rule);
        await toasts.Act(toasts.Quiet.Single());

        toasts.Errors.Should().ContainSingle().Which.Message.Should().Be($"The rule could not be put back. {Reason}");
        part.Own.Should().BeEmpty();
        var retry = toasts.Quiet.Should().ContainSingle().Subject;
        (retry.Message, retry.Action!.Label, retry.Action.Name).Should().Be(("Rule removed: Sample rule.", "Undo", "Undo removing Sample rule"));

        Server.Answer("POST", $"{bot}/rules", rule, HttpStatusCode.Created);
        await toasts.Act(retry);

        Server.Sent("POST", $"{bot}/rules").Should().HaveCount(2);
        part.Own.Should().ContainSingle().Which.Name.Should().Be("Sample rule");
    }

    [Fact]
    public async Task Undo_from_another_screen_puts_the_rule_back_on_its_own_bot()
    {
        var rule = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["critical"] });
        Server.Answer("GET", $"{bot}/rules", (BotRuleResponse[])[rule]);
        Server.Answer("DELETE", $"{bot}/rules/{ruleId}", HttpStatusCode.NoContent);
        Server.Answer("POST", $"{bot}/rules", rule, HttpStatusCode.Created);
        var editor = await OpenEditor("Rules, 1 rule");
        editor.Toggle(Rules, true);
        var part = editor.RulesPart;
        using var toasts = new ToastHostViewModel(Toasts);

        await part.Remove(rule);
        editor.Close();

        await toasts.Act(toasts.Quiet.Single());

        Server.Sent("POST", $"{bot}/rules").Should().ContainSingle().Which.Should().Contain("\"name\":\"Sample rule\"");
        Toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be("Rule put back: Sample rule.");
        part.Own.Should().BeEmpty("the section that was left does not gain a rule");
        part.Announcement.Should().Be("Rule removed: Sample rule.");
    }

    #endregion

    #region States

    [Fact]
    public async Task A_person_who_may_not_edit_is_told_so_and_shown_the_way_back()
    {
        Server.Refuse("GET", $"{bot}/definition", HttpStatusCode.Forbidden, "Forbidden");
        var editor = Make<BotEditorViewModel>();

        await editor.Open(botId, null);

        editor.State.Should().Be(BotEditorState.NotPermitted);
        BotEditorViewModel.NotPermittedTitle.Should().Be("You cannot change this bot");
        BotEditorViewModel.NotPermittedAddress.Should().Be(Addresses.Home);
        editor.Draft.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_bot_says_not_found_and_offers_the_bots_list()
    {
        Server.Answer("GET", $"{bot}/definition", HttpStatusCode.NotFound);
        var editor = Make<BotEditorViewModel>();

        await editor.Open(botId, null);

        editor.State.Should().Be(BotEditorState.Missing);
        BotEditorViewModel.MissingTitle.Should().Be("Bot not found");
        BotEditorViewModel.MissingAction.Should().Be("Back to bots");
    }

    [Fact]
    public async Task A_server_that_fails_says_so_and_offers_to_try_again()
    {
        Server.Answer("GET", $"{bot}/definition", HttpStatusCode.ServiceUnavailable);
        var editor = Make<BotEditorViewModel>();

        await editor.Open(botId, null);

        editor.State.Should().Be(BotEditorState.Failed);
        BotEditorViewModel.LoadFailedTitle.Should().Be("Drover could not load this bot");
        BotEditorViewModel.TryAgain.Should().Be("Try again");

        Server.Answer("GET", $"{bot}/definition", Marshal);
        await editor.LoadCommand.ExecuteAsync(null);
        editor.State.Should().Be(BotEditorState.Shown);
    }

    [Fact]
    public async Task The_editor_says_it_is_loading_until_the_bot_is_read()
    {
        var held = Server.Hold("GET", $"{bot}/definition");
        var editor = Make<BotEditorViewModel>();

        var opening = editor.Open(botId, null);
        await Eventually(() => Server.Requests.Any(request => request.Path == $"{bot}/definition"));
        editor.State.Should().Be(BotEditorState.Loading);
        BotEditorViewModel.Loading.Should().Be("Loading the bot.");

        held.SetResult();
        await opening;
        editor.State.Should().Be(BotEditorState.Shown);
    }

    [Fact]
    public async Task Pausing_and_resuming_keeps_what_is_typed_and_says_so()
    {
        Server.Answer("POST", $"{bot}/pause", Marshal with { Status = "paused", Name = "Not typed" });
        var editor = await OpenEditor();
        editor.Rename("Scout");

        await editor.Pause();

        editor.IsPaused.Should().BeTrue();
        editor.Draft!.Name.Should().Be("Scout", "only the status is taken from the answer");
        Toasts.Toasts.Select(toast => toast.Message).Should().Contain("Marshal is paused. It starts no run until it is resumed.");
    }

    #endregion

    #region Sandbox Grants

    [Fact]
    public async Task A_sandbox_the_bot_may_not_use_says_so_and_one_tap_stages_a_grant_for_each_sandbox_tool_that_save_applies()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("PUT", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var sandboxTools = SandboxToolEntries("off", "allow", null);
        Server.Answer("GET", $"{bot}/tools", sandboxTools);

        foreach (var tool in sandboxTools)
        {
            Server.Answer("PUT", $"{bot}/tools/{tool.ToolId}", tool with { Grant = "allow" });
        }

        var editor = await OpenEditor();
        editor.Summary(Sandbox).Should().Be("Private · network off · not allowed yet");
        editor.Toggle(Sandbox, true);
        var part = editor.SandboxPart;

        SandboxSectionViewModel.SandboxGroupName.Should().Be("What this bot may do in its sandbox");
        part.SandboxGrantShown.Should().BeTrue();
        part.SandboxGrantWords.Should().Contain("This bot may not use its sandbox yet.");
        part.SandboxGrantAction.Should().Be("Allow sandbox tools");
        part.AllowSandboxTools();

        part.SandboxGrantWords.Should().Contain("The sandbox tools will be allowed when you save.");
        Server.Requests.Should().NotContain(request => request.Method == "PUT", "nothing is granted until the person saves");
        await editor.Save();

        Server.Requests.Count(request => request.Method == "PUT" && request.Path.Contains("/tools/", StringComparison.Ordinal)).Should().Be(sandboxTools.Length);
        foreach (var tool in sandboxTools)
        {
            Server.Sent("PUT", $"{bot}/tools/{tool.ToolId}").Should().ContainSingle()
                .Which.Should().Be(JsonSerializer.Serialize(new SetToolGrantRequest("allow"), JsonSerializerOptions.Web), "the same request the Tools section sends");
        }

        part.SandboxGrantWords.Should().Contain("May run commands and use files.");
    }

    [Fact]
    public async Task Discarding_drops_the_staged_grants()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("off", "allow", null));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.AllowSandboxTools();
        editor.Discard();

        editor.SandboxPart.SandboxGrantWords.Should().Contain("may not use its sandbox yet");
        Server.Requests.Should().NotContain(request => request.Method == "PUT");
        editor.ShowsSaveBar.Should().BeFalse();
    }

    [Fact]
    public async Task Allowed_sandbox_tools_are_said_quietly_with_a_way_to_the_tools_section()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("allow", "allow", null));
        var editor = await OpenEditor();
        editor.Summary(Sandbox).Should().Be("Private · network off · may run commands");
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.SandboxGrantWords.Should().Contain("May run commands and use files.");
        editor.SandboxPart.SandboxGrantAction.Should().Be(SandboxSectionViewModel.OpenTools);
        editor.Toggle(Tools, true);

        editor.IsOpen(Tools).Should().BeTrue("the Tools section opens");
    }

    [Fact]
    public async Task An_optional_tool_that_is_off_does_not_hide_existing_sandbox_grants()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var sandboxTools = SandboxToolEntries("allow", "allow", null);
        Server.Answer("GET", $"{bot}/tools", sandboxTools.Select(tool =>
            tool.Name == "sandbox.share_file" ? tool with { Grant = "off" } : tool).ToArray());

        var editor = await OpenEditor();
        editor.Summary(Sandbox).Should().Be("Private · network off · some tools allowed");
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.SandboxGrantWords.Should().Contain("Some sandbox tools are allowed.");
        editor.SandboxPart.SandboxGrantAction.Should().Be("Allow remaining sandbox tools");
    }

    [Fact]
    public async Task A_parent_that_keeps_the_tools_off_is_named_and_nothing_is_offered()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("off", "off", "Sample parent"));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.SandboxGrantWords.Should().Contain("Sample parent does not allow the sandbox tools, so this bot cannot use its sandbox.");
        editor.SandboxPart.SandboxGrantAction.Should().BeNull();
    }

    [Fact]
    public async Task Under_a_parent_that_only_asks_the_tap_stages_ask()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("off", "ask", "Sample parent"));
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);

        editor.SandboxPart.AllowSandboxTools();

        editor.Draft!.StagedGrants.Values.Should().AllBe("ask");
        editor.SandboxPart.SandboxGrantWords.Should().Contain("will be allowed when you save");
    }

    [Fact]
    public async Task With_a_screen_the_screen_tools_are_offered_and_a_model_that_cannot_see_is_named_with_a_way_to_the_model_section()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        Server.Answer("GET", $"{bot}/tools", (BotToolEntry[])[.. SandboxToolEntries("allow", "allow", null), .. ScreenToolEntries("off")]);
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);
        var part = editor.SandboxPart;

        SandboxSectionViewModel.ScreenGroupName.Should().Be("What this bot may do on its screen");
        part.ScreenGrantShown.Should().BeTrue();
        part.ScreenGrantWords.Should().Contain("This bot may not use the screen yet.");
        SandboxSectionViewModel.TakeOver.Should().Be("Asking a person to take over the screen is a tool of its own, allowed in the Tools section.");
        part.ScreenGrantAction.Should().Be("Allow screen tools");
        part.CannotSeeShown.Should().BeTrue();
        SandboxSectionViewModel.CannotSee.Should().Be("The model this bot uses cannot see images, so the bot cannot use the screen. In the Model section, choose a model to read the screen for it, or a model that can see.");
        SandboxSectionViewModel.OpenModel.Should().Be("Open the Model section");

        part.AllowScreenTools();
        editor.Toggle(Model, true);

        part.ScreenGrantWords.Should().Contain("The screen tools will be allowed when you save.");
        editor.IsOpen(Model).Should().BeTrue();
    }

    [Fact]
    public async Task A_text_only_bot_can_choose_a_model_of_another_connection_to_read_its_screen_in_the_model_section()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection, EyesConnection]);
        Server.Answer("GET", $"{space}/model-connections/{eyesConnectionId}/models", (ModelCatalogResponse[])[CatalogModel("sample-eyes", toolCalling: true, vision: true), CatalogModel("sample-blind", toolCalling: true)]);
        Server.Answer("PUT", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var editor = await OpenEditor();
        editor.Toggle(Sandbox, true);
        editor.SandboxPart.CannotSeeShown.Should().BeTrue();
        editor.Toggle(Model, true);
        var part = editor.ModelPart;

        part.ReaderShown.Should().BeTrue();
        part.ReaderShows.Should().Be(ReaderShows.Choice);
        ModelSectionViewModel.ReaderLabel.Should().Be("Reads the screen for this bot");
        part.ReaderOptions.Select(option => option.Label).Should().Equal("Its own model (cannot read images)", "sample-eyes");
        part.ReaderOptions[1].Group.Should().Be("Eyes connection");
        part.ReaderValue.Should().BeEmpty();

        part.ChooseReader($"{eyesConnectionId}/sample-eyes");

        editor.SandboxPart.CannotSeeShown.Should().BeFalse();
        editor.SandboxPart.ReadByShown.Should().BeTrue();
        editor.SandboxPart.ReadBy.Should().Be("sample-eyes reads the screen for this bot. Change it in the Model section.");
        editor.Summary(Sandbox).Should().Contain("screen read by sample-eyes", "the closed section says which model reads the screen");
        await editor.Save();
        Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which.Should()
            .Contain($"\"screenReader\":{{\"modelConnectionId\":\"{eyesConnectionId}\",\"model\":\"sample-eyes\"}}");
    }

    [Fact]
    public async Task A_text_only_bot_without_a_screen_is_offered_a_screen_reader_with_a_way_to_give_it_a_screen()
    {
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection, EyesConnection]);
        Server.Answer("GET", $"{space}/model-connections/{eyesConnectionId}/models", (ModelCatalogResponse[])[CatalogModel("sample-eyes", toolCalling: true, vision: true)]);
        var editor = await OpenEditor();
        editor.Toggle(Model, true);
        var part = editor.ModelPart;

        part.ReaderOptions.Select(option => option.Label).Should().Equal("Its own model (cannot read images)", "sample-eyes");
        part.NeedsScreenShown.Should().BeTrue();
        ModelSectionViewModel.NeedsScreen.Should().Be("It is used when this bot has a screen. Give it a sandbox with Desktop on, in the Sandbox section.");
        ModelSectionViewModel.OpenSandbox.Should().Be("Open the Sandbox section");
        editor.Toggle(Sandbox, true);

        editor.IsOpen(Sandbox).Should().BeTrue();
    }

    [Fact]
    public async Task A_chosen_screen_reader_is_shown_with_what_it_costs_and_in_the_summary()
    {
        var chosen = new SandboxSettingsBody("off", [], null, Desktop: true, new ScreenReaderBody(eyesConnectionId, "sample-eyes"));
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = chosen });
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection, EyesConnection]);
        Server.Answer("GET", $"{space}/model-connections/{eyesConnectionId}/models", (ModelCatalogResponse[])[CatalogModel("sample-eyes", toolCalling: true, vision: true)]);
        var editor = await OpenEditor();

        editor.Summary(Sandbox).Should().Contain("Private · network off · desktop · screen read by sample-eyes");
        editor.Toggle(Model, true);

        editor.ModelPart.ReaderValue.Should().Be($"{eyesConnectionId}/sample-eyes");
        editor.ModelPart.ReaderHint.Should().Be("The bot gets a description of each screenshot in words instead of the picture. That is slower and less exact than a model that sees for itself, and each screenshot is also a call to sample-eyes.");
        editor.Toggle(Sandbox, true);
        editor.SandboxPart.CannotSeeShown.Should().BeFalse();
    }

    [Fact]
    public async Task A_space_with_no_model_that_reads_images_says_so_and_links_to_model_connections()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        var editor = await OpenEditor();
        editor.Toggle(Model, true);

        editor.ModelPart.ReaderShown.Should().BeTrue();
        editor.ModelPart.ReaderShows.Should().Be(ReaderShows.NoneToChoose, "there is no list to choose from");
        ModelSectionViewModel.NoReaders.Should().Be("This model cannot see images, and no model of this space is listed as reading them, so no other model can read the screen for this bot.");
        ModelSectionViewModel.ManageConnections.Should().Be("Manage model connections");
        ModelSectionViewModel.ManageConnectionsAddress.Should().Be(Addresses.Models);
        editor.Toggle(Sandbox, true);
        editor.SandboxPart.CannotSeeShown.Should().BeTrue();
    }

    [Fact]
    public async Task A_bot_that_sees_or_has_no_screen_and_no_reader_to_choose_is_not_asked_about_reading_it()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var editor = await OpenEditor();
        editor.Toggle(Model, true);
        editor.Toggle(Sandbox, true);

        editor.ModelPart.ReaderShown.Should().BeFalse();
        editor.SandboxPart.ReadByShown.Should().BeFalse();
        editor.SandboxPart.CannotSeeShown.Should().BeFalse();
    }

    #endregion

    #region Words

    [Fact]
    public void The_way_back_names_the_bots_list()
    {
        BotEditorViewModel.Back.Should().Be("Back to bots");
    }

    #endregion

    #region Helpers

    private static ModelCatalogResponse CatalogModel(string id, bool toolCalling, bool vision = false) =>
        new(
            id,
            id,
            new ModelCapabilitiesResponse(null, null, toolCalling, true, vision, toolCalling, toolCalling ? ["low", "high"] : [], toolCalling, toolCalling),
            new ModelPriceResponse(null, null, null, "USD"),
            null,
            [],
            "available",
            DateTimeOffset.UnixEpoch);

    private static BotToolEntry[] SandboxToolEntries(string grant, string ceiling, string? parent) =>
        [.. SandboxGrants.SandboxTools.Select((name, index) =>
            new BotToolEntry(Guid.Parse($"0196f1a0-0000-7000-8000-0000000001{index:00}"), name, name, "Sample description", "builtin", null, null, "write", grant, ceiling, parent, "available"))];

    private static BotToolEntry[] ScreenToolEntries(string grant) =>
        [.. SandboxGrants.ScreenTools.Select((name, index) =>
            new BotToolEntry(Guid.Parse($"0196f1a0-0000-7000-8000-0000000002{index:00}"), name, name, "Sample description", "builtin", null, null, "write", grant, "allow", null, "available"))];

    private static BotToolEntry TimeTool(string grant, string ceiling, string? parent) =>
        new(toolId, "time.now", "Current time", "Sample description", "builtin", null, null, "read", grant, ceiling, parent, "available");

    private static BotRuleResponse Rule(string name, string checkpoint, string effect, params RuleConditionBody[] conditions) =>
        new(ruleId, botId, "Marshal", false, name, true, 0, checkpoint, effect, conditions);

    // Opens the editor and checks it read everything, as the web tests wait for a section's summary.
    private static BotSummary Made(string handle = "new-bot") =>
        new(madeId, "New bot", handle, string.Empty, connectionId, "sample-large", "active");

    // The request id a create request carries, or null when it carries none.
    private static Guid? RequestId(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("requestId", out var id) && id.ValueKind is JsonValueKind.String ? id.GetGuid() : null;
    }

    private static BotSummary BotSummaryOf(Guid id, string name) =>
        new(id, name, name.ToLowerInvariant(), "Sample instructions", connectionId, "sample-large", "active");

    private async Task<BotEditorViewModel> OpenEditor(string loaded = "Rules, No rules")
    {
        var editor = Make<BotEditorViewModel>();
        await editor.Open(botId, null);
        var summaries = BotEditorViewModel.Sections.Select(section => $"{section}, {editor.Summary(section)}").ToList();
        summaries.Should().Contain(loaded).And.NotContain("Routines, Loading");
        return editor;
    }

    #endregion
}
