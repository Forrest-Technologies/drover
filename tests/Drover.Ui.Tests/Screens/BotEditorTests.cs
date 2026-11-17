namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The bot editor against a stand-in server: what it shows at rest, what each section announces, what it sends,
/// and what it says when the server refuses. Every name and value here is a sample.
/// </summary>
public sealed class BotEditorTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid ruleId = Guid.Parse("0196f1a0-0000-7000-8000-000000000007");

    private static readonly Guid eyesConnectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000008");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly string bot = $"/api/spaces/{SpaceId}/bots/{botId}";

    #endregion

    #region Constructors

    public BotEditorTests()
    {
        SignedIn();
        Server.Answer("GET", $"{bot}/definition", Marshal);
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        Server.Answer("GET", $"{space}/model-connections/{connectionId}/models", new[] { Model("sample-large", toolCalling: true), Model("sample-plain", toolCalling: false) });
        Server.Answer("GET", $"{space}/tool-connections", Array.Empty<ToolConnectionSummary>());
        Server.Answer("GET", $"{bot}/tools", new[] { TimeTool("allow", "allow", null) });
        Server.Answer("GET", $"{bot}/schedules", Array.Empty<BotScheduleResponse>());
        Server.Answer("GET", $"{bot}/rules", Array.Empty<BotRuleResponse>());
        Server.Answer("GET", $"{bot}/rules/options", new BotRuleOptions(true));
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: true, MaxIdleMinutes: 60, Desktop: true));
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
        Person.UserId,
        null);

    #endregion

    #region At Rest

    [Fact]
    public async Task Removing_a_bot_with_bots_beneath_asks_whether_they_go_too_and_sends_the_answer()
    {
        var helper = new BotSummary(Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1"), "Sample helper", "sample-helper", string.Empty, connectionId, "sample-large", "active", botId);
        var deeper = helper with { Id = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2"), Name = "Sample deeper", ParentBotId = helper.Id };
        Server.Answer("GET", $"{space}/bots", new[] { helper, deeper });
        Server.Answer("DELETE", $"{space}/bots/{botId}", HttpStatusCode.NoContent);
        var page = RenderEditor();
        var dialog = await AskToRemove(page);
        dialog.TextContent.Should().Contain("2 bots work beneath it. Remove them too, or keep them? Kept bots move up under the top of the crew.");
        var choices = dialog.QuerySelectorAll("button.dv-button").Select(button => button.TextContent.Trim()).ToList();
        choices.Should().Equal("Keep bot", "Remove it, keep the rest", "Remove all 3");

        await dialog.QuerySelectorAll("button.dv-button")[1].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Server.Addresses.Should().Contain($"DELETE {space}/bots/{botId}?beneath=keep"));
    }

    [Fact]
    public async Task Removing_a_bot_with_nothing_beneath_asks_nothing_more()
    {
        var page = RenderEditor();

        var dialog = await AskToRemove(page);

        dialog.QuerySelectorAll("button.dv-button").Select(button => button.TextContent.Trim()).Should().Equal("Keep bot", "Remove bot");
    }

    [Fact]
    public void The_editor_shows_who_the_bot_is_and_seven_closed_sections_each_with_its_summary_in_its_name()
    {
        var page = RenderEditor();

        page.Find("h1").TextContent.Should().Be("Edit Marshal");
        page.Find(".dv-avatar").GetAttribute("aria-label").Should().Be("Marshal");
        page.FindAll("form > .dv-textarea .dv-textarea-label").Select(label => label.TextContent).Should().Equal("What this bot is for", "Instructions");
        var toggles = page.FindAll(".dv-disclosure-toggle");
        toggles.Select(toggle => toggle.GetAttribute("aria-expanded")).Should().AllBe("false");
        toggles.Select(toggle => Words(toggle.TextContent)).Should().Equal(
            "Model, Sample connection · sample-large",
            "Tools, 1 allowed",
            "Rules, No rules",
            "Memory, Does not remember · keeps every message",
            "Routines, No routines",
            "Sandbox, No sandbox",
            "Team, The space's caps");
        page.FindAll(".dv-disclosure-body").Should().OnlyContain(body => body.HasAttribute("hidden") && body.ChildElementCount == 0, "a closed section shows no settings");
        page.FindAll(".dv-editor-save").Should().BeEmpty("there is nothing to save yet");
    }

    [Fact]
    public async Task A_section_opens_from_its_button_and_says_so_to_assistive_technology()
    {
        var page = RenderEditor();
        var toggle = page.FindAll(".dv-disclosure-toggle")[0];

        await toggle.ClickAsync(new MouseEventArgs());

        toggle = page.FindAll(".dv-disclosure-toggle")[0];
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        page.Find($"#{toggle.GetAttribute("aria-controls")}").HasAttribute("hidden").Should().BeFalse();
        Words(toggle.TextContent).Should().Be("Model", "an open section shows its settings instead of the summary");
        page.FindAll(".dv-disclosure-open label").Select(label => label.TextContent).Should().Contain(["Connection", "Model", "Temperature"]);
    }

    #endregion

    #region Model And Tools

    [Fact]
    public async Task Only_the_settings_the_model_has_are_shown()
    {
        var page = RenderEditor();
        await Open(page, 0);

        var plain = Labels(page);
        await page.FindAll(".dv-disclosure-open select")[1].ChangeAsync(new ChangeEventArgs { Value = "sample-plain" });
        var none = Labels(page);

        plain.Should().Contain(["Reasoning effort", "Answer as a JSON object"]).And.Contain(label => label.Contains("web search", StringComparison.OrdinalIgnoreCase));
        none.Should().NotContain("Reasoning effort").And.NotContain("Answer as a JSON object");
    }

    [Fact]
    public async Task A_model_without_tool_calling_hides_the_tool_controls_and_says_why()
    {
        var page = RenderEditor();
        await Open(page, 0);
        await page.FindAll(".dv-disclosure-open select")[1].ChangeAsync(new ChangeEventArgs { Value = "sample-plain" });
        await Open(page, 0);

        Words(page.FindAll(".dv-disclosure-toggle")[1].TextContent).Should().Be("Tools, This model cannot use tools");
        await Open(page, 1);

        page.FindAll(".dv-disclosure-open .dv-segmented").Should().BeEmpty();
        page.Find(".dv-disclosure-open [role=status]").TextContent.Should().Contain("cannot call tools").And.Contain("Model section");
    }

    [Fact]
    public async Task Each_tool_is_off_ask_or_allow_and_a_change_is_sent_at_once_and_announced()
    {
        Server.Answer("PUT", $"{bot}/tools/{toolId}", TimeTool("ask", "allow", null));
        var page = RenderEditor();
        await Open(page, 1);

        page.Find(".dv-segmented legend").TextContent.Should().Be("Current time for Marshal");
        page.FindAll(".dv-segment").Select(segment => Words(segment.TextContent)).Should().Equal("Off", "Ask", "Allow");
        await page.FindAll(".dv-segment-input")[1].ChangeAsync(new ChangeEventArgs { Value = "on" });

        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-open [role=status]").Select(status => status.TextContent).Should().Contain("Current time is now ask."));
        Server.Sent("PUT", $"{bot}/tools/{toolId}").Should().ContainSingle().Which.Should().Contain("\"grant\":\"ask\"");
        page.FindAll(".dv-segment-input")[1].HasAttribute("checked").Should().BeTrue();
    }

    [Fact]
    public async Task A_tool_the_parent_lacks_cannot_be_turned_on_and_says_whose_it_is()
    {
        Server.Answer("GET", $"{bot}/tools", new[] { TimeTool("off", "off", "Sample parent") });
        var page = RenderEditor();
        await Open(page, 1);

        page.Find(".dv-tool-limit").TextContent.Should().Be("Sample parent does not have this.");
        page.FindAll(".dv-segment-input").Select(input => input.HasAttribute("disabled")).Should().Equal(false, true, true);
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
        Server.Answer("GET", $"{space}/tool-connections", new[]
        {
            new ToolConnectionSummary(connection, "Sample server", "sample", "https://tools.example.test/mcp", "none", "untrusted", "automatic", "connected", null, null, null),
            new ToolConnectionSummary(Guid.Parse("0196f1a0-0000-7000-8000-00000000000b"), "Quiet server", "quiet", "https://quiet.example.test/mcp", "none", "untrusted", "automatic", "failed", null, null, "Sample failure."),
        });
        Server.Answer("GET", $"{bot}/tools", new[] { TimeTool("allow", "allow", null), changed, gone });
        Server.Answer("POST", $"{space}/tool-connections/{connection}/tools/{changed.ToolId}/review", new ConnectionToolSummary(changed.ToolId, connection, "lookup", "sample_lookup", "Look up", "Sample new words", "write", "available", DateTimeOffset.UnixEpoch));
        var page = Render<BotEditor>(parameters => parameters.Add(editor => editor.BotId, botId));
        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-toggle").Select(toggle => Words(toggle.TextContent)).Should().Contain("Tools, 1 allowed · 1 ask first · 1 to review"));
        await Open(page, 1);

        page.FindAll(".dv-tools-group > summary").Select(summary => Words(summary.TextContent)).Should().Equal(
            "Built in 1 of 1 on",
            "Sample server 2 of 2 on · 1 to review",
            "Quiet server 0 of 0 on");
        page.FindAll(".dv-tools-group > .dv-editor-hint").Should().ContainSingle().Which.TextContent.Should().Be("Quiet server has listed no tools. Sample failure.");
        page.Find(".dv-tool-review .dv-badge").TextContent.Should().Be("Changed, needs review");
        page.Find(".dv-tool-review").TextContent.Should().Contain("Marshal asks before each use");
        page.FindAll(".dv-tool-compare dd").Select(words => words.TextContent).Should().Equal("Look upSample old words", "Look upSample new words");
        page.FindAll(".dv-tool-limit").Select(limit => limit.TextContent).Should().Contain("Sample server no longer lists this tool, so it cannot be used.");

        Server.Answer("GET", $"{bot}/tools", new[] { TimeTool("allow", "allow", null), changed with { NeedsReview = false, Reviewed = null }, gone });
        await page.Find(".dv-tool-review .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-tool-review").Should().BeEmpty());
        Server.Sent("POST", $"{space}/tool-connections/{connection}/tools/{changed.ToolId}/review").Should().ContainSingle();
        page.FindAll(".dv-disclosure-open [role=status]").Select(status => status.TextContent).Should().Contain("The change to Look up is accepted.");
    }

    [Fact]
    public async Task A_grant_the_server_refuses_is_said_in_its_words_and_the_control_goes_back()
    {
        Server.Refuse("PUT", $"{bot}/tools/{toolId}", HttpStatusCode.Conflict, "Sample parent has this tool set to ask. Raise it on Sample parent first.");
        var page = RenderEditor();
        await Open(page, 1);

        await page.FindAll(".dv-segment-input")[0].ChangeAsync(new ChangeEventArgs { Value = "on" });

        page.WaitForAssertion(() => page.Find(".dv-disclosure-open [role=alert]").TextContent.Should().Contain("Raise it on Sample parent first."));
        page.FindAll(".dv-segment-input")[2].HasAttribute("checked").Should().BeTrue("the grant that still holds is shown");
    }

    #endregion

    #region Saving

    [Fact]
    public async Task An_edit_shows_the_save_bar_and_saving_sends_the_whole_definition()
    {
        Server.Answer("PUT", $"{bot}/definition", Marshal with { Name = "Scout" });
        var page = RenderEditor();

        await page.Find(".dv-editor-names input").InputAsync(new ChangeEventArgs { Value = "Scout" });

        page.Find(".dv-editor-save-words").TextContent.Should().Be("Changes not saved yet");
        page.Find(".dv-avatar").GetAttribute("aria-label").Should().Be("Scout", "the character follows the name as it is typed");
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.FindAll(".dv-editor-save").Should().BeEmpty());
        var sent = Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Subject;
        sent.Should().Contain("\"name\":\"Scout\"").And.Contain("\"handle\":\"scout\"", "the handle follows the name until someone chooses it").And.Contain("\"sandboxPolicy\":\"none\"").And.Contain("\"modelSettings\"");
        page.Find("h1").TextContent.Should().Be("Edit Scout");
        page.FindAll("p[role=status]").Select(status => status.TextContent).Should().Contain("Saved");
    }

    [Fact]
    public async Task A_refused_save_says_what_the_server_said_and_keeps_what_was_typed()
    {
        Server.Refuse("PUT", $"{bot}/definition", HttpStatusCode.BadRequest, "Give the bot a name of up to 200 characters.");
        var page = RenderEditor();

        await page.Find(".dv-editor-names input").InputAsync(new ChangeEventArgs { Value = " " });
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-editor-save [role=alert]").TextContent.Should().Contain("Give the bot a name"));
        page.Find(".dv-editor-names input").GetAttribute("value").Should().Be(" ");
    }

    [Fact]
    public async Task The_save_bar_marks_a_problem_so_a_phone_gives_it_a_row_of_its_own()
    {
        Server.Refuse("PUT", $"{bot}/definition", HttpStatusCode.BadRequest, "Give the bot a name of up to 200 characters.");
        var page = RenderEditor();

        await page.Find(".dv-editor-names input").InputAsync(new ChangeEventArgs { Value = "Scout" });
        page.Find(".dv-editor-save").ClassList.Should().NotContain("dv-editor-save-problem");
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-editor-save").ClassList.Should().Contain("dv-editor-save-problem"));
        page.Find(".dv-editor-save [role=alert]").TextContent.Should().Contain("Give the bot a name");
        page.Find(".dv-editor-save").QuerySelectorAll("button").Select(button => button.TextContent.Trim())
            .Should().Equal("Discard", "Save changes");
    }

    [Fact]
    public async Task A_number_that_is_not_a_number_is_said_before_anything_is_sent()
    {
        var page = RenderEditor();
        await Open(page, 6);

        await page.FindAll(".dv-disclosure-open input")[0].InputAsync(new ChangeEventArgs { Value = "many" });
        await page.Find("form").SubmitAsync();

        page.Find(".dv-editor-save [role=alert]").TextContent.Should().Contain("The number of bots it may build is a whole number.");
        Server.Sent("PUT", $"{bot}/definition").Should().BeEmpty();
    }

    #endregion

    #region Sandbox

    [Fact]
    public async Task The_sandbox_section_says_no_provider_is_set_up_only_when_the_server_has_none()
    {
        var configured = RenderEditor();
        await Open(configured, 5);
        var configuredNotes = configured.FindAll(".dv-disclosure-open [role=status]");

        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: false, OpenNetwork: true, MaxIdleMinutes: 60, Desktop: true));
        var bare = RenderEditor();
        await Open(bare, 5);

        configuredNotes.Should().NotBeEmpty("the live region is there before any message, so a later one is announced")
            .And.OnlyContain(note => string.IsNullOrWhiteSpace(note.TextContent));
        bare.WaitForAssertion(() => bare.FindAll(".dv-disclosure-open [role=status]").Select(note => Words(note.TextContent))
            .Should().Contain(text => text.StartsWith("No sandbox provider is set up on this server", StringComparison.Ordinal)));
        Labels(bare).Should().Contain("Sandbox", "the choice is still offered, and saved for when a provider is added");
    }

    [Fact]
    public async Task The_sandbox_section_says_how_the_sandbox_is_now_and_leads_to_its_screen()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/sandbox", new BotSandboxStatus(true, true, "paused", DateTimeOffset.UtcNow.AddMinutes(-10.5), Desktop: true, BotWorking: false, DesktopOffered: true));
        var page = RenderEditor();
        await Open(page, 5);

        var live = page.Find(".dv-disclosure-open .dv-editor-sandbox-live");
        Words(live.QuerySelector("span")!.TextContent).Should().Be("Paused · used 10 minutes ago");
        var link = live.QuerySelector("a")!;
        link.TextContent.Should().Be("Open the sandbox");
        link.GetAttribute("href").Should().Be(Addresses.BotSandbox(botId));
    }

    [Fact]
    public async Task The_sandbox_section_leaves_the_state_out_when_it_cannot_be_read()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var page = RenderEditor();
        await Open(page, 5);

        page.FindAll(".dv-editor-sandbox-live").Should().BeEmpty();
        Labels(page).Should().Contain("Sandbox", "the settings stay whatever the state");
    }

    [Fact]
    public async Task Open_network_is_not_offered_where_the_server_forbids_it_and_the_editor_says_so()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: false, MaxIdleMinutes: 15, Desktop: false));
        var page = RenderEditor();
        await Open(page, 5);

        var network = page.FindAll(".dv-disclosure-open .dv-segmented").Single(group => Words(group.QuerySelector("legend")!.TextContent) == "Sandbox network");
        network.QuerySelectorAll(".dv-segment").Select(segment => Words(segment.TextContent)).Should().Equal("Off", "Named hosts");
        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(hint => hint.TextContent).Should().Contain("Open internet access is not available on this server.");
        page.FindAll(".dv-disclosure-open input[placeholder]").Select(input => input.GetAttribute("placeholder")).Should().Contain("Up to 15");
    }

    [Fact]
    public async Task Open_network_is_offered_where_the_server_allows_it()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var page = RenderEditor();
        await Open(page, 5);

        var network = page.FindAll(".dv-disclosure-open .dv-segmented").Single(group => Words(group.QuerySelector("legend")!.TextContent) == "Sandbox network");
        network.QuerySelectorAll(".dv-segment").Select(segment => Words(segment.TextContent)).Should().Equal("Off", "Named hosts", "Open");
        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(hint => hint.TextContent).Should().NotContain("Open internet access is not available on this server.");
    }

    [Fact]
    public async Task The_desktop_switch_says_what_it_costs_and_shows_and_is_saved_with_the_sandbox()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("PUT", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var page = RenderEditor();
        await Open(page, 5);

        var desktop = page.FindAll(".dv-disclosure-open input[role=switch]").Single(input => Words(input.ParentElement!.TextContent) == "Desktop");
        desktop.GetAttribute("type").Should().Be("checkbox");
        desktop.HasAttribute("checked").Should().BeFalse();
        var hint = page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(found => found.TextContent).Single(text => text.Contains("screen", StringComparison.Ordinal));
        hint.Should().Contain("costs more to run").And.Contain("can show anything on that screen").And.Contain("replaces the sandbox");
        hint.Should().NotContainEquivalentOf("e2b", "the interface names no vendor");

        await desktop.ChangeAsync(new ChangeEventArgs { Value = true });
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which.Should().Contain("\"desktop\":true"));
    }

    [Fact]
    public async Task The_desktop_switch_is_not_offered_where_the_server_forbids_it_and_the_editor_says_so()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: false, MaxIdleMinutes: 15, Desktop: false));
        var page = RenderEditor();
        await Open(page, 5);

        page.FindAll(".dv-disclosure-open input[role=switch]").Should().NotContain(input => Words(input.ParentElement!.TextContent) == "Desktop");
        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(hint => hint.TextContent).Should().Contain("A sandbox with a screen is not available on this server.");
    }

    [Fact]
    public async Task A_bot_saved_with_a_screen_keeps_the_switch_where_the_server_forbids_one_so_it_can_be_turned_off()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        Server.Answer("GET", $"{bot}/sandbox/options", new BotSandboxOptions(Available: true, OpenNetwork: false, MaxIdleMinutes: 15, Desktop: false));
        var page = RenderEditor();
        await Open(page, 5);

        page.FindAll(".dv-disclosure-open input[role=switch]").Single(input => Words(input.ParentElement!.TextContent) == "Desktop").HasAttribute("checked").Should().BeTrue();
        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(hint => hint.TextContent).Should().Contain("A sandbox with a screen is not available on this server.");
    }

    #endregion

    #region Routines

    [Fact]
    public async Task With_no_routines_the_section_says_what_one_is_and_offers_the_one_button()
    {
        var page = RenderEditor();
        await Open(page, 4);

        page.Find(".dv-disclosure-open .dv-editor-hint").TextContent.Should().Be("A routine is a prompt Marshal is sent on its own, at the times you choose.");
        page.FindAll(".dv-disclosure-open .dv-routine-add button").Select(button => button.TextContent.Trim()).Should().Equal("Add a routine");
        page.FindAll(".dv-routine").Should().BeEmpty();
        page.Find(".dv-routine-add").Should().NotBeNull();
    }

    [Fact]
    public async Task A_routine_is_written_from_plain_choices_saved_off_and_listed_as_its_sentence()
    {
        var made = Routine(enabled: false) with { Cron = "30 9 * * 1-5", TimeZone = "Europe/Berlin" };
        Server.Answer("POST", $"{bot}/schedules", made, HttpStatusCode.Created);
        Server.Answer("POST", $"{bot}/schedules/check", new ScheduleCheckResponse(null, []));
        var page = RenderEditor();
        await Open(page, 4);

        await page.Find(".dv-disclosure-open .dv-button").ClickAsync(new MouseEventArgs());
        await page.Find(".dv-routine-form textarea").InputAsync(new ChangeEventArgs { Value = "Sample prompt" });
        await page.Find(".dv-routine-form select").ChangeAsync(new ChangeEventArgs { Value = "Weekdays" });
        await page.Find(".dv-routine-form input[type=time]").InputAsync(new ChangeEventArgs { Value = "09:30" });
        await Field(page, "Time zone").InputAsync(new ChangeEventArgs { Value = "Europe/Berlin" });
        page.Find(".dv-when-reads").TextContent.Should().Be("Reads as: Every weekday at 09:30, Berlin time.");
        await page.Find(".dv-routine-form .dv-button-primary").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-routine-when").TextContent.Should().Be("Every weekday at 09:30, Berlin time"));
        Server.Sent("POST", $"{bot}/schedules").Should().ContainSingle().Which.Should()
            .Contain("\"cron\":\"30 9 * * 1-5\"").And.Contain("\"timeZone\":\"Europe/Berlin\"").And.Contain("\"enabled\":false");
        page.Find(".dv-routine-prompt").TextContent.Should().Be("Sample prompt");
        page.Find(".dv-disclosure-open [role=status]").TextContent.Should().StartWith("Routine added, turned off");
    }

    [Fact]
    public async Task A_custom_expression_is_explained_or_refused_by_the_server_before_it_is_saved()
    {
        Server.Refuse("POST", $"{bot}/schedules", HttpStatusCode.BadRequest, "not expected");
        Server.Answer("POST", $"{bot}/schedules/check", new ScheduleCheckResponse("Schedules on this server run at most once every 60 minutes. Choose a time that repeats less often.", []));
        var page = RenderEditor();
        await Open(page, 4);

        await page.Find(".dv-disclosure-open .dv-button").ClickAsync(new MouseEventArgs());
        await page.Find(".dv-routine-form select").ChangeAsync(new ChangeEventArgs { Value = "Custom" });
        await Field(page, "Cron expression").InputAsync(new ChangeEventArgs { Value = "*/20 * * * *" });

        page.WaitForAssertion(() => page.Find(".dv-when [role=alert]").TextContent.Trim().Should().Be(
            "Schedules on this server run at most once every 60 minutes. Choose a time that repeats less often."));
        page.Find(".dv-when-reads").TextContent.Should().Be("The expression */20 * * * * cannot be saved as it is.");
        Server.Sent("POST", $"{bot}/schedules/check").Last().Should().Contain("\"cron\":\"*/20 * * * *\"");
        Server.Sent("POST", $"{bot}/schedules").Should().BeEmpty("nothing is saved until the person saves");
    }

    [Fact]
    public async Task A_routine_the_server_refuses_on_save_says_why_by_the_buttons()
    {
        const string Refusal = "A space on this server holds at most 2 schedules. Remove one first.";
        Server.Refuse("POST", $"{bot}/schedules", HttpStatusCode.BadRequest, Refusal);
        Server.Answer("POST", $"{bot}/schedules/check", new ScheduleCheckResponse(null, []));
        var page = RenderEditor();
        await Open(page, 4);

        await page.Find(".dv-disclosure-open .dv-button").ClickAsync(new MouseEventArgs());
        page.Find(".dv-routine-form").TextContent.Should().Contain("It is saved turned off. Run it once to try it, then turn it on.");
        await page.Find(".dv-routine-form textarea").InputAsync(new ChangeEventArgs { Value = "Sample prompt" });
        await page.Find(".dv-routine-form .dv-button-primary").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-routine-form > .dv-note-region")[0].TextContent.Trim().Should().Be(Refusal));
        page.Find(".dv-when [role=alert]").TextContent.Trim().Should().BeEmpty("a refusal that is not about the time is not said beside it");
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
        Server.Answer("GET", $"{bot}/schedules", new[] { failed });
        Server.Answer("POST", $"{bot}/schedules/{failed.Id}/run-once", new ScheduleRunOnceResponse(threadId, Guid.CreateVersion7()), HttpStatusCode.Accepted);
        var page = RenderEditor("Routines, 1 routine");
        await Open(page, 4);

        page.Find(".dv-routine-facts").TextContent.Should().Contain("Failed").And.Contain(": The model connection could not be reached.");
        page.Find(".dv-routine-facts a").GetAttribute("href").Should().Be($"/threads/{threadId}");
        await page.FindAll(".dv-routine-actions button").Single(button => button.TextContent.Trim() == "Run once now").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-routine-started a").GetAttribute("href").Should().Be($"/threads/{threadId}"));
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
        Server.Answer("GET", $"{bot}/schedules", new[] { theirs });
        Server.Answer("POST", $"{bot}/schedules/{theirs.Id}/run-once", new ScheduleRunOnceResponse(Guid.CreateVersion7(), Guid.CreateVersion7()), HttpStatusCode.Accepted);
        var page = RenderEditor("Routines, 1 routine");
        await Open(page, 4);

        await page.FindAll(".dv-routine-actions button").Single(button => button.TextContent.Trim() == "Run once now").ClickAsync(new MouseEventArgs());

        Server.Sent("POST", $"{bot}/schedules/{theirs.Id}/run-once").Should().BeEmpty("someone who did not write it reads it first");
        page.Find(".dv-routine-run-prompt").TextContent.Should().Be("Sample prompt.\nSecond line of the sample.");
        await page.FindAll("button").Single(button => button.TextContent.Trim() == "Run it once now").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Server.Sent("POST", $"{bot}/schedules/{theirs.Id}/run-once").Should().ContainSingle());
        page.WaitForAssertion(() => page.Find(".dv-routine-started a").TextContent.Should().Be("Open the conversation it started"));
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>("focus moves to the link that took the disabled button's place");
    }

    [Fact]
    public async Task A_refused_run_of_another_persons_routine_puts_focus_back_on_run_once_now_beside_the_reason()
    {
        const string Refusal = "Marshal is paused, so its routines do not run. Resume it first.";
        const string Focus = "Blazor._internal.domWrapper.focus";
        var theirs = Routine(enabled: true) with { OwnerUserId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1") };
        Server.Answer("GET", $"{bot}/schedules", new[] { theirs });
        Server.Refuse("POST", $"{bot}/schedules/{theirs.Id}/run-once", HttpStatusCode.Conflict, Refusal);
        var page = RenderEditor("Routines, 1 routine");
        await Open(page, 4);

        await page.FindAll(".dv-routine-actions button").Single(button => button.TextContent.Trim() == "Run once now").ClickAsync(new MouseEventArgs());
        var before = JSInterop.Invocations.Count(call => call.Identifier == Focus);
        await page.FindAll("button").Single(button => button.TextContent.Trim() == "Run it once now").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-routine [role=alert]").TextContent.Trim().Should().Be(Refusal));
        page.FindAll(".dv-routine-actions button").Single(button => button.TextContent.Trim() == "Run once now").HasAttribute("disabled").Should().BeFalse();
        page.FindAll(".dv-routine-started").Should().BeEmpty("there is no conversation to go to");
        JSInterop.Invocations.Count(call => call.Identifier == Focus).Should().Be(before + 1,
            "focus moves once, after the answer is in, to the only place left: Run once now, enabled again beside the refusal");
    }

    [Fact]
    public async Task Removing_a_routine_asks_first()
    {
        var routine = Routine(enabled: true);
        Server.Answer("GET", $"{bot}/schedules", new[] { routine });
        Server.Answer("DELETE", $"{bot}/schedules/{routine.Id}", HttpStatusCode.NoContent);
        var page = RenderEditor("Routines, 1 routine");
        await Open(page, 4);

        await page.FindAll(".dv-routine-actions button").Single(button => button.TextContent.Trim() == "Remove").ClickAsync(new MouseEventArgs());

        Server.Sent("DELETE", $"{bot}/schedules/{routine.Id}").Should().BeEmpty("removing asks first");
        await page.FindAll("button").Single(button => button.TextContent.Trim() == "Remove routine").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => page.FindAll(".dv-routine").Should().BeEmpty());
        Server.Sent("DELETE", $"{bot}/schedules/{routine.Id}").Should().ContainSingle();
    }

    [Fact]
    public void Another_admins_routine_offers_no_change_and_a_member_reads_routines_without_controls()
    {
        var theirs = Routine(enabled: true) with { OwnerUserId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1") };
        Services.GetRequiredService<SessionState>().Begin(Person);

        var asOwner = Render<RoutinesSection>(set => set
            .Add(view => view.SpaceId, SpaceId).Add(view => view.BotId, botId).Add(view => view.BotName, "Marshal")
            .Add(view => view.Schedules, [theirs]));

        asOwner.FindAll(".dv-routine-actions button, .dv-routine-add button").Select(button => button.TextContent.Trim()).Should().Equal(
            "Run once now", "Remove", "Add a routine");
        asOwner.FindAll("[role=switch]").Should().ContainSingle("another admin may turn it off");

        Services.GetRequiredService<SessionState>().Begin(Person with { Spaces = [Person.Spaces[0] with { Role = "member" }] });
        var asMember = Render<RoutinesSection>(set => set
            .Add(view => view.SpaceId, SpaceId).Add(view => view.BotId, botId).Add(view => view.BotName, "Marshal")
            .Add(view => view.Schedules, [theirs]));

        asMember.FindAll(".dv-routine button, .dv-routine-add, [role=switch]").Should().BeEmpty("a member reads routines");
        asMember.Find(".dv-routine-when").TextContent.Should().Be("Every day at 09:00, UTC");
        var emptyForMember = Render<RoutinesSection>(set => set
            .Add(view => view.SpaceId, SpaceId).Add(view => view.BotId, botId).Add(view => view.BotName, "Marshal")
            .Add(view => view.Schedules, []));
        emptyForMember.Find(".dv-editor-hint").TextContent.Should().Be("Marshal has no routines. Owners and admins of this space add them.");
    }

    private BotScheduleResponse Routine(bool enabled) =>
        new(Guid.Parse("0196f1a0-0000-7000-8000-000000000006"), botId, Person.UserId, "0 9 * * *", "UTC", "Sample prompt", "new_thread", enabled,
            enabled ? new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero) : null);

    #endregion

    #region Rules

    [Fact]
    public async Task With_no_rules_the_section_offers_to_write_one_and_starters_that_say_what_they_are()
    {
        var page = RenderEditor();
        await Open(page, 2);

        page.FindAll(".dv-disclosure-open .dv-rule").Should().BeEmpty();
        page.FindAll(".dv-disclosure-open .dv-disclosure-body button").Select(button => Words(button.TextContent)).Should().Equal(
            "Write a rule",
            "Ask before anything that writes",
            "Block common forms of a card number or key",
            "Quiet hours");
        page.Find(".dv-rule-form legend").TextContent.Should().Be("Starters. Each adds a rule you can then change.");
        page.FindAll(".dv-rule-try").Should().BeEmpty("there is nothing to try yet");
    }

    [Fact]
    public async Task A_starter_adds_its_rule_and_the_rule_is_announced_and_shown_as_its_sentence()
    {
        Server.Answer("POST", $"{bot}/rules", Rule("Ask before anything that writes", RuleWords.BeforeTool, "ask", new RuleConditionBody("tool_kind") { ToolKinds = ["write", "critical"] }), HttpStatusCode.Created);
        var page = RenderEditor();
        await Open(page, 2);

        await page.FindAll(".dv-rule-starts button")[1].ClickAsync(new MouseEventArgs());

        const string Sentence = "When a tool is about to run, if the tool writes or is critical, then ask a person first.";
        page.WaitForAssertion(() => page.Find(".dv-rule-sentence").TextContent.Should().Be(Sentence));
        page.Find(".dv-disclosure-open p[role=status]").TextContent.Should().Be($"Rule added. {Sentence}");
        Server.Sent("POST", $"{bot}/rules").Should().ContainSingle().Which.Should().Contain("\"checkpoint\":\"before_tool\"").And.Contain("\"effect\":\"ask\"").And.Contain("\"toolKinds\":[\"write\",\"critical\"]");
        page.Find(".dv-rule input[role=switch]").ParentElement!.TextContent.Should().Contain("On").And.Contain("Ask before anything that writes");
        page.FindAll(".dv-rule-actions button").Select(button => button.GetAttribute("aria-label")).Should().Equal(
            "Change the rule Ask before anything that writes",
            "Remove the rule Ask before anything that writes");
    }

    [Fact]
    public async Task Where_the_server_runs_no_written_patterns_the_section_says_so_and_does_not_offer_one()
    {
        Server.Answer("GET", $"{bot}/rules/options", new BotRuleOptions(false));
        var page = RenderEditor();
        await Open(page, 2);

        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(hint => hint.TextContent).Should().Contain(RuleWords.WrittenPatternsOff);

        await page.Find(".dv-rule-starts button").ClickAsync(new MouseEventArgs());
        await Select(page, "When").ChangeAsync(new ChangeEventArgs { Value = RuleWords.BeforeReply });
        await page.FindAll(".dv-rule-form .dv-button").First(button => button.TextContent.Contains("Add a condition")).ClickAsync(new MouseEventArgs());

        Options(page, "If").Should().Equal(
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
        var page = RenderEditor();
        await Open(page, 2);
        await page.Find(".dv-rule-starts button").ClickAsync(new MouseEventArgs());
        Labels(page).Should().StartWith(["New rule", "When", "Condition 1", "If"]);
        Options(page, "Then").Should().Equal("Ask a person first", "Do not run it", "Remove what matched", "Stop the run");

        await Select(page, "When").ChangeAsync(new ChangeEventArgs { Value = RuleWords.BeforeReply });

        page.FindAll(".dv-rule-condition").Should().BeEmpty("a condition about a tool means nothing for a reply");
        Options(page, "Then").Should().Equal(["Do not send it", "Remove what matched", "Stop the run"], "a reply cannot wait for a person");
        await page.FindAll(".dv-rule-form .dv-button").First(button => button.TextContent.Contains("Add a condition")).ClickAsync(new MouseEventArgs());
        Options(page, "If").Should().Equal(
            "The reply contains (catches common forms)",
            "The reply holds (catches common forms)",
            "The reply matches a pattern (catches common forms)",
            "The time is between",
            "The run has used more than");
        await page.Find(".dv-rule-condition textarea").InputAsync(new ChangeEventArgs { Value = "sample words" });
        page.Find(".dv-rule-condition .dv-editor-hint").TextContent.Should().Be(RuleWords.NotCaught, "a person is told what a condition on text does not find");
        await Select(page, "Then").ChangeAsync(new ChangeEventArgs { Value = "redact" });
        await page.FindAll(".dv-rule-form input[type=text]")[^1].InputAsync(new ChangeEventArgs { Value = "Sample rule" });
        Words(page.Find(".dv-rule-preview").TextContent).Should().Be("Reads as When the bot is about to reply, if the reply contains \"sample words\" (catches common forms), then remove what matched.");
        await page.Find(".dv-rule-form .dv-button-primary").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-rule-sentence").TextContent.Should().Be("When the bot is about to reply, if the reply contains \"sample words\" (catches common forms), then remove what matched."));
        Server.Sent("POST", $"{bot}/rules").Should().ContainSingle().Which.Should()
            .Contain("\"name\":\"Sample rule\"").And.Contain("\"checkpoint\":\"before_reply\"").And.Contain("\"effect\":\"redact\"").And.Contain("\"entries\":[\"sample words\"]");
    }

    [Fact]
    public async Task The_section_says_which_conditions_are_exact_and_which_catch_common_forms()
    {
        var page = RenderEditor();
        await Open(page, 2);

        Words(page.Find(".dv-disclosure-open .dv-editor-hint").TextContent).Should().Be(
            "The server checks a rule every time, whatever the bot's instructions say. Conditions about the tool, the time, counts, and named inputs are exact. Conditions that look for something in text catch common forms and are not a guarantee.");
        page.Find(".dv-disclosure-open").TextContent.Should().NotContainAny("hard limit", "never leaks", "guarantees that");
    }

    [Fact]
    public async Task What_the_server_notes_about_a_rule_is_shown_with_it_and_read_out_when_it_is_saved()
    {
        const string Note = "No tool has that name now: sample.typo. The rule does not apply to it until one does. Check the spelling.";
        var noted = Rule("Sample rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_is") { Tools = ["sample.typo"] }) with { Notes = [Note] };
        Server.Answer("GET", $"{bot}/rules", new[] { noted });
        Server.Answer("PUT", $"{bot}/rules/{ruleId}", noted);
        var page = RenderEditor("Rules, 1 rule");
        await Open(page, 2);

        page.Find(".dv-rule .dv-rule-note").TextContent.Should().Contain(Note);

        await page.Find(".dv-rule input[role=switch]").ChangeAsync(new ChangeEventArgs { Value = false });

        page.WaitForAssertion(() => page.Find(".dv-disclosure-open p[role=status]").TextContent.Should().EndWith(Note));
    }

    [Fact]
    public async Task A_trial_decided_by_a_pattern_condition_says_it_is_not_a_guarantee()
    {
        Server.Answer("GET", $"{bot}/rules", new[] { Rule("Sample rule", RuleWords.BeforeReply, "block", new RuleConditionBody("text_holds") { Builtin = "card_number" }) });
        Server.Answer("POST", $"{bot}/rules/try", new TryRulesResponse("block", ruleId, "Sample rule", "the text holds a card number", null, null, ["Sample rule"]) { CatchesCommonForms = true });
        var page = RenderEditor("Rules, 1 rule");
        await Open(page, 2);

        await Select(page, "As if").ChangeAsync(new ChangeEventArgs { Value = RuleWords.BeforeReply });
        await page.Find(".dv-rule-try .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Words(page.Find(".dv-rule-trial").TextContent).Should().Be(
            "The rule \"Sample rule\" would block this: the text holds a card number. Some of the rules tried look for something in text: they catch common forms and are not a guarantee."));
    }

    [Fact]
    public async Task A_list_condition_starts_on_the_fields_a_recipient_can_be_in_and_the_address_form_is_marked_a_pattern()
    {
        var page = RenderEditor();
        await Open(page, 2);
        await page.Find(".dv-rule-starts button").ClickAsync(new MouseEventArgs());
        Options(page, "If").Should().Contain(["An input is not on a list", "An email address is not on a list (catches common forms)"]);

        await Select(page, "If").ChangeAsync(new ChangeEventArgs { Value = "not_in_list" });

        page.Find(".dv-rule-condition input[type=text]").GetAttribute("value").Should().Be("to, cc, bcc");
        Words(page.Find(".dv-rule-preview").TextContent).Should().StartWith("Reads as When a tool is about to run, if any of the inputs \"to\", \"cc\", \"bcc\" is not one of");
    }

    [Fact]
    public async Task A_rule_the_server_refuses_stays_in_the_builder_with_the_servers_reason()
    {
        Server.Refuse("POST", $"{bot}/rules", HttpStatusCode.BadRequest, "Give the rule a name.");
        var page = RenderEditor();
        await Open(page, 2);
        await page.Find(".dv-rule-starts button").ClickAsync(new MouseEventArgs());

        await page.Find(".dv-rule-form .dv-button-primary").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-rule-form [role=alert]").TextContent.Should().Contain("Give the rule a name."));
        page.FindAll(".dv-rule-form .dv-button-primary").Should().ContainSingle("the builder stays open");
    }

    [Fact]
    public async Task Rules_from_the_bots_above_are_listed_with_where_they_come_from_and_cannot_be_changed_here()
    {
        var parent = Rule("Sample parent rule", RuleWords.BeforeTool, "block", new RuleConditionBody("tool_kind") { ToolKinds = ["critical"] }) with { BotName = "Sample parent", Inherited = true };
        var own = Rule("Sample own rule", RuleWords.MessageArrives, "stop", new RuleConditionBody("text_holds") { Builtin = "key" }) with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000008"), Enabled = false };
        Server.Answer("GET", $"{bot}/rules", new[] { parent, own });
        var page = RenderEditor("Rules, 2 rules, 1 off");
        await Open(page, 2);

        var lists = page.FindAll(".dv-rules");
        lists.Select(list => list.GetAttribute("aria-label")).Should().Equal("Rules from the bots above", "Rules");
        Words(lists[0].TextContent).Should().Be("When a tool is about to run, if the tool is critical, then do not run it. Sample parent rule · from Sample parent, changed there");
        lists[0].QuerySelectorAll("button, input").Should().BeEmpty("an inherited rule has no control here");
        lists[1].QuerySelector(".dv-rule-sentence")!.TextContent.Should().Be("When a message arrives, if the message holds a key or token (catches common forms), then stop the run.");
        lists[1].QuerySelector("input[role=switch]")!.HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public async Task Trying_a_sample_asks_the_server_and_shows_its_answer_in_words()
    {
        Server.Answer("GET", $"{bot}/rules", new[] { Rule("Sample rule", RuleWords.BeforeReply, "redact", new RuleConditionBody("text_contains") { Entries = ["sample words"] }) });
        Server.Answer("POST", $"{bot}/rules/try", new TryRulesResponse("redact", ruleId, "Sample rule", "the text contains text the rule names", "Some [removed by a rule] here.", null, ["Sample rule"]));
        var page = RenderEditor("Rules, 1 rule");
        await Open(page, 2);

        await Select(page, "As if").ChangeAsync(new ChangeEventArgs { Value = RuleWords.BeforeReply });
        await page.Find(".dv-rule-try textarea").InputAsync(new ChangeEventArgs { Value = "Some sample words here." });
        await page.Find(".dv-rule-try .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-rule-trial > *").Select(line => Words(line.TextContent)).Should().Equal(
            "The rule \"Sample rule\" would remove what matched: the text contains text the rule names.",
            "What would be left",
            "Some [removed by a rule] here."));
        page.Find(".dv-rule-trial").GetAttribute("role").Should().Be("status");
        Server.Sent("POST", $"{bot}/rules/try").Should().ContainSingle().Which.Should()
            .Contain("\"checkpoint\":\"before_reply\"").And.Contain("\"text\":\"Some sample words here.\"").And.NotContain("\"draft\":{");
    }

    #endregion

    #region States

    [Fact]
    public void A_person_who_may_not_edit_is_told_so_and_shown_the_way_back()
    {
        Server.Refuse("GET", $"{bot}/definition", HttpStatusCode.Forbidden, "Forbidden");

        var page = Render<BotEditor>(parameters => parameters.Add(editor => editor.BotId, botId));

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("You cannot change this bot"));
        page.Find(".dv-empty-action").GetAttribute("href").Should().Be(Addresses.Home);
        page.FindAll("form").Should().BeEmpty();
    }

    [Fact]
    public void A_bot_that_is_not_there_says_so()
    {
        Server.Answer("GET", $"{bot}/definition", HttpStatusCode.NotFound);

        var page = Render<BotEditor>(parameters => parameters.Add(editor => editor.BotId, botId));

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("This bot is not here"));
    }

    [Fact]
    public void A_server_that_fails_says_so_and_offers_to_try_again()
    {
        Server.Answer("GET", $"{bot}/definition", HttpStatusCode.ServiceUnavailable);

        var page = Render<BotEditor>(parameters => parameters.Add(editor => editor.BotId, botId));

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load this bot"));
        page.Find(".dv-empty .dv-button").TextContent.Trim().Should().Be("Try again");
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

        var page = RenderEditor();
        Words(page.FindAll(".dv-disclosure-toggle")[5].TextContent).Should().Be("Sandbox, Private · network off · not allowed yet");
        await Open(page, 5);

        var group = page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']");
        group.TextContent.Should().Contain("This bot may not use its sandbox yet.");
        var allow = group.QuerySelector("button")!;
        allow.TextContent.Trim().Should().Be("Allow sandbox tools");
        await allow.ClickAsync(new MouseEventArgs());

        page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']").TextContent
            .Should().Contain("The sandbox tools will be allowed when you save.");
        Server.Requests.Should().NotContain(request => request.Method == "PUT", "nothing is granted until the person saves");
        await page.FindAll(".dv-editor-save button").Single(button => button.TextContent.Trim() == "Save changes").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Server.Requests.Count(request => request.Method == "PUT" && request.Path.Contains("/tools/", StringComparison.Ordinal)).Should().Be(sandboxTools.Length));
        foreach (var tool in sandboxTools)
        {
            Server.Sent("PUT", $"{bot}/tools/{tool.ToolId}").Should().ContainSingle()
                .Which.Should().Be(JsonSerializer.Serialize(new SetToolGrantRequest("allow"), JsonSerializerOptions.Web), "the same request the Tools section sends");
        }

        page.WaitForAssertion(() => page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']").TextContent
            .Should().Contain("May run commands and use files."));
    }

    [Fact]
    public async Task Discarding_drops_the_staged_grants()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("off", "allow", null));
        var page = RenderEditor();
        await Open(page, 5);

        await page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox'] button").ClickAsync(new MouseEventArgs());
        await page.FindAll(".dv-editor-save button").Single(button => button.TextContent.Trim() == "Discard").ClickAsync(new MouseEventArgs());

        page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']").TextContent.Should().Contain("may not use its sandbox yet");
        Server.Requests.Should().NotContain(request => request.Method == "PUT");
        page.FindAll(".dv-editor-save").Should().BeEmpty();
    }

    [Fact]
    public async Task Allowed_sandbox_tools_are_said_quietly_with_a_way_to_the_tools_section()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("allow", "allow", null));
        var page = RenderEditor();
        Words(page.FindAll(".dv-disclosure-toggle")[5].TextContent).Should().Be("Sandbox, Private · network off · may run commands");
        await Open(page, 5);

        var group = page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']");
        group.TextContent.Should().Contain("May run commands and use files.");
        await group.QuerySelector("button")!.ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-toggle")[1].GetAttribute("aria-expanded").Should().Be("true", "the Tools section opens"));
    }

    [Fact]
    public async Task An_optional_tool_that_is_off_does_not_hide_existing_sandbox_grants()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var sandboxTools = SandboxToolEntries("allow", "allow", null);
        Server.Answer("GET", $"{bot}/tools", sandboxTools.Select(tool =>
            tool.Name == "sandbox.share_file" ? tool with { Grant = "off" } : tool).ToArray());

        var page = RenderEditor();
        Words(page.FindAll(".dv-disclosure-toggle")[5].TextContent).Should().Be("Sandbox, Private · network off · some tools allowed");
        await Open(page, 5);

        var group = page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']");
        group.TextContent.Should().Contain("Some sandbox tools are allowed.");
        group.QuerySelector("button")!.TextContent.Trim().Should().Be("Allow remaining sandbox tools");
    }

    [Fact]
    public async Task A_parent_that_keeps_the_tools_off_is_named_and_nothing_is_offered()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("off", "off", "Sample parent"));
        var page = RenderEditor();
        await Open(page, 5);

        var group = page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']");
        group.TextContent.Should().Contain("Sample parent does not allow the sandbox tools, so this bot cannot use its sandbox.");
        group.QuerySelectorAll("button").Should().BeEmpty();
    }

    [Fact]
    public async Task Under_a_parent_that_only_asks_the_tap_stages_ask()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        Server.Answer("GET", $"{bot}/tools", SandboxToolEntries("off", "ask", "Sample parent"));
        var page = RenderEditor();
        await Open(page, 5);

        await page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox'] button").ClickAsync(new MouseEventArgs());

        SandboxGrants.Stage(SandboxToolEntries("off", "ask", "Sample parent"), SandboxGrants.SandboxTools, new Dictionary<Guid, string>())
            .Values.Should().AllBe("ask");
        page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do in its sandbox']").TextContent
            .Should().Contain("will be allowed when you save");
    }

    [Fact]
    public async Task With_a_screen_the_screen_tools_are_offered_and_a_model_that_cannot_see_is_named_with_a_way_to_the_model_section()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        Server.Answer("GET", $"{bot}/tools", (BotToolEntry[])[.. SandboxToolEntries("allow", "allow", null), .. ScreenToolEntries("off")]);
        var page = RenderEditor();
        await Open(page, 5);

        var screen = page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do on its screen']");
        screen.TextContent.Should().Contain("This bot may not use the screen yet.");
        screen.TextContent.Should().Contain("Asking a person to take over the screen is a tool of its own, allowed in the Tools section.");
        screen.QuerySelector("button")!.TextContent.Trim().Should().Be("Allow screen tools");
        var note = page.FindAll(".dv-disclosure-open [role=status]").Select(found => Words(found.TextContent));
        note.Should().Contain("The model this bot uses cannot see images, so the bot cannot use the screen. In the Model section, choose a model to read the screen for it, or a model that can see.");
        var openModel = page.FindAll(".dv-disclosure-open .dv-editor-sandbox-grant button").Single(button => button.TextContent.Trim() == "Open the Model section");

        await screen.QuerySelector("button")!.ClickAsync(new MouseEventArgs());
        await openModel.ClickAsync(new MouseEventArgs());

        page.Find(".dv-disclosure-open [role=group][aria-label='What this bot may do on its screen']").TextContent.Should().Contain("The screen tools will be allowed when you save.");
        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-toggle")[0].GetAttribute("aria-expanded").Should().Be("true"));
    }

    [Fact]
    public async Task A_text_only_bot_can_choose_a_model_of_another_connection_to_read_its_screen_in_the_model_section()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection, EyesConnection });
        Server.Answer("GET", $"{space}/model-connections/{eyesConnectionId}/models", new[] { Model("sample-eyes", toolCalling: true, vision: true), Model("sample-blind", toolCalling: true) });
        Server.Answer("PUT", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var page = RenderEditor();
        await Open(page, 5);
        page.FindAll(".dv-disclosure-open [role=status]").Select(found => Words(found.TextContent))
            .Should().Contain(text => text.Contains("cannot use the screen", StringComparison.Ordinal));
        await page.FindAll(".dv-disclosure-open .dv-editor-sandbox-grant button").Single(button => button.TextContent.Trim() == "Open the Model section")
            .ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Labels(page).Should().Contain("Reads the screen for this bot"));
        var reader = Select(page, "Reads the screen for this bot");
        reader.Id.Should().NotBeEmpty();
        page.Find($"label[for='{reader.Id}']").TextContent.Should().Be("Reads the screen for this bot");
        Options(page, "Reads the screen for this bot").Should().Equal("Its own model (cannot read images)", "sample-eyes");
        reader.QuerySelector("optgroup")!.GetAttribute("label").Should().Be("Eyes connection");
        reader.GetAttribute("value").Should().BeEmpty();

        await reader.ChangeAsync(new ChangeEventArgs { Value = $"{eyesConnectionId}/sample-eyes" });

        page.FindAll(".dv-disclosure-open [role=status]").Select(found => Words(found.TextContent)).Should().NotContain(text => text.Contains("cannot use the screen", StringComparison.Ordinal));
        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(found => Words(found.TextContent))
            .Should().Contain("sample-eyes reads the screen for this bot. Change it in the Model section.");
        await Open(page, 5);
        page.FindAll(".dv-disclosure-toggle")[5].TextContent.Should().Contain("screen read by sample-eyes", "the closed section says which model reads the screen");
        await page.Find("form").SubmitAsync();
        page.WaitForAssertion(() => Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Which.Should()
            .Contain($"\"screenReader\":{{\"modelConnectionId\":\"{eyesConnectionId}\",\"model\":\"sample-eyes\"}}"));
    }

    [Fact]
    public async Task A_text_only_bot_without_a_screen_is_offered_a_screen_reader_with_a_way_to_give_it_a_screen()
    {
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection, EyesConnection });
        Server.Answer("GET", $"{space}/model-connections/{eyesConnectionId}/models", new[] { Model("sample-eyes", toolCalling: true, vision: true) });
        var page = RenderEditor();
        await Open(page, 0);

        page.WaitForAssertion(() => Options(page, "Reads the screen for this bot").Should().Equal("Its own model (cannot read images)", "sample-eyes"));
        page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(found => Words(found.TextContent))
            .Should().Contain("It is used when this bot has a screen. Give it a sandbox with Desktop on, in the Sandbox section.");
        await page.FindAll(".dv-disclosure-open .dv-editor-sandbox-grant button").Single(button => button.TextContent.Trim() == "Open the Sandbox section")
            .ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-toggle")[5].GetAttribute("aria-expanded").Should().Be("true"));
    }

    [Fact]
    public async Task A_chosen_screen_reader_is_shown_with_what_it_costs_and_in_the_summary()
    {
        var chosen = new SandboxSettingsBody("off", [], null, Desktop: true, new ScreenReaderBody(eyesConnectionId, "sample-eyes"));
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = chosen });
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection, EyesConnection });
        Server.Answer("GET", $"{space}/model-connections/{eyesConnectionId}/models", new[] { Model("sample-eyes", toolCalling: true, vision: true) });
        var page = RenderEditor();

        page.FindAll(".dv-disclosure-toggle")[5].TextContent.Should().Contain("Private · network off · desktop · screen read by sample-eyes");
        await Open(page, 0);

        page.WaitForAssertion(() => Select(page, "Reads the screen for this bot").GetAttribute("value").Should().Be($"{eyesConnectionId}/sample-eyes"));
        var hint = page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(found => Words(found.TextContent))
            .Single(text => text.Contains("description", StringComparison.Ordinal));
        hint.Should().Be("The bot gets a description of each screenshot in words instead of the picture. That is slower and less exact than a model that sees for itself, and each screenshot is also a call to sample-eyes.");
        await Open(page, 5);
        page.FindAll(".dv-disclosure-open [role=status]").Select(found => Words(found.TextContent)).Should().NotContain(text => text.Contains("cannot use the screen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_space_with_no_model_that_reads_images_says_so_and_links_to_model_connections()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private", SandboxSettings = new SandboxSettingsBody("off", [], null, Desktop: true) });
        var page = RenderEditor();
        await Open(page, 0);

        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-open .dv-editor-hint").Select(found => Words(found.TextContent))
            .Should().Contain("This model cannot see images, and no model of this space is listed as reading them, so no other model can read the screen for this bot."));
        Labels(page).Should().NotContain("Reads the screen for this bot");
        var link = page.FindAll(".dv-disclosure-open a").Single(anchor => anchor.TextContent.Trim() == "Manage model connections");
        link.GetAttribute("href").Should().Be(Addresses.Models);
        await Open(page, 5);
        page.FindAll(".dv-disclosure-open [role=status]").Select(found => Words(found.TextContent))
            .Should().Contain("The model this bot uses cannot see images, so the bot cannot use the screen. In the Model section, choose a model to read the screen for it, or a model that can see.");
    }

    [Fact]
    public async Task A_bot_that_sees_or_has_no_screen_and_no_reader_to_choose_is_not_asked_about_reading_it()
    {
        Server.Answer("GET", $"{bot}/definition", Marshal with { SandboxPolicy = "private" });
        var page = RenderEditor();
        await Open(page, 0);
        await Open(page, 5);

        Labels(page).Should().NotContain("Reads the screen for this bot");
        page.Find(".dv-disclosure-open").TextContent.Should().NotContain("reading them");
        page.FindAll(".dv-disclosure-open")[1].TextContent.Should().NotContain("reads the screen");
    }

    #endregion

    #region Helpers

    // Chooses Remove in the editor's menu and returns the dialog that asks about it.
    private static async Task<IElement> AskToRemove(IRenderedComponent<BotEditor> page)
    {
        page.WaitForElement("[role=menuitem]");
        await page.FindAll("[role=menuitem]").First(item => item.TextContent.Trim() == "Remove").ClickAsync(new MouseEventArgs());
        return page.FindAll("dialog").First(candidate => candidate.TextContent.Contains("Remove Marshal", StringComparison.Ordinal));
    }

    private static string Words(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Labels(IRenderedComponent<BotEditor> page) =>
        [.. page.FindAll(".dv-disclosure-open label, .dv-disclosure-open legend").Select(label => Words(label.TextContent))];

    private static ModelCatalogResponse Model(string id, bool toolCalling, bool vision = false) =>
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

    private static IElement Select(IRenderedComponent<BotEditor> page, string label) =>
        page.FindAll(".dv-disclosure-open .dv-select").First(select => Words(select.QuerySelector("label")!.TextContent) == label).QuerySelector("select")!;

    private static IElement Field(IRenderedComponent<BotEditor> page, string label) =>
        page.FindAll(".dv-disclosure-open .dv-field").First(field => Words(field.QuerySelector("label")!.TextContent) == label).QuerySelector("input")!;

    private static List<string> Options(IRenderedComponent<BotEditor> page, string label) =>
        [.. Select(page, label).QuerySelectorAll("option").Select(option => option.TextContent)];

    private static Task Open(IRenderedComponent<BotEditor> page, int section) =>
        page.FindAll(".dv-disclosure-toggle")[section].ClickAsync(new MouseEventArgs());

    private IRenderedComponent<BotEditor> RenderEditor(string loaded = "Rules, No rules")
    {
        var page = Render<BotEditor>(parameters => parameters.Add(editor => editor.BotId, botId));
        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-toggle").Select(toggle => Words(toggle.TextContent)).Should().Contain(toggle => toggle.StartsWith("Routines, ") && toggle != "Routines, Loading").And.Contain(loaded));
        return page;
    }

    #endregion
}
