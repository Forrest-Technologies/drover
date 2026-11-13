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

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly string bot = $"/api/spaces/{SpaceId}/bots/{botId}";

    #endregion

    #region Constructors

    public BotEditorTests()
    {
        SignedIn();
        Server.Answer("GET", $"{bot}/definition", Atlas);
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        Server.Answer("GET", $"{space}/model-connections/{connectionId}/models", new[] { Model("sample-large", toolCalling: true), Model("sample-plain", toolCalling: false) });
        Server.Answer("GET", $"{bot}/tools", new[] { TimeTool("allow", "allow", null) });
        Server.Answer("GET", $"{bot}/schedules", Array.Empty<BotScheduleResponse>());
    }

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", "sample-large", [], null, null);

    private static BotDetail Atlas { get; } = new(
        botId,
        "Atlas",
        "atlas",
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
    public void The_editor_shows_who_the_bot_is_and_six_closed_sections_each_with_its_summary_in_its_name()
    {
        var page = RenderEditor();

        page.Find("h1").TextContent.Should().Be("Edit Atlas");
        page.Find(".dv-avatar").GetAttribute("aria-label").Should().Be("Atlas");
        page.FindAll("form > .dv-textarea .dv-textarea-label").Select(label => label.TextContent).Should().Equal("What this bot is for", "Instructions");
        var toggles = page.FindAll(".dv-disclosure-toggle");
        toggles.Select(toggle => toggle.GetAttribute("aria-expanded")).Should().AllBe("false");
        toggles.Select(toggle => Words(toggle.TextContent)).Should().Equal(
            "Model, Sample connection · sample-large",
            "Tools, 1 allowed",
            "Memory, Does not remember · keeps every message",
            "Schedule, None",
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

        page.Find(".dv-segmented legend").TextContent.Should().Be("Current time for Atlas");
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
        Server.Answer("PUT", $"{bot}/definition", Atlas with { Name = "Scout" });
        var page = RenderEditor();

        await page.Find(".dv-editor-names input").InputAsync(new ChangeEventArgs { Value = "Scout" });

        page.Find(".dv-editor-save-words").TextContent.Should().Be("Changes not saved yet");
        page.Find(".dv-avatar").GetAttribute("aria-label").Should().Be("Scout", "the character follows the name as it is typed");
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.FindAll(".dv-editor-save").Should().BeEmpty());
        var sent = Server.Sent("PUT", $"{bot}/definition").Should().ContainSingle().Subject;
        sent.Should().Contain("\"name\":\"Scout\"").And.Contain("\"handle\":\"atlas\"").And.Contain("\"sandboxPolicy\":\"none\"").And.Contain("\"modelSettings\"");
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
    public async Task A_number_that_is_not_a_number_is_said_before_anything_is_sent()
    {
        var page = RenderEditor();
        await Open(page, 5);

        await page.FindAll(".dv-disclosure-open input")[0].InputAsync(new ChangeEventArgs { Value = "many" });
        await page.Find("form").SubmitAsync();

        page.Find(".dv-editor-save [role=alert]").TextContent.Should().Contain("The number of bots it may build is a whole number.");
        Server.Sent("PUT", $"{bot}/definition").Should().BeEmpty();
    }

    #endregion

    #region Schedules

    [Fact]
    public async Task A_schedule_is_made_from_plain_choices_and_shown_as_a_sentence()
    {
        var made = new BotScheduleResponse(Guid.Parse("0196f1a0-0000-7000-8000-000000000006"), botId, Person.UserId, "30 9 * * 1-5", "UTC", "Sample prompt", "new_thread", true, null);
        Server.Answer("POST", $"{bot}/schedules", made, HttpStatusCode.Created);
        var page = RenderEditor();
        await Open(page, 3);

        await page.Find(".dv-disclosure-open .dv-button").ClickAsync(new MouseEventArgs());
        await page.Find(".dv-schedule-form select").ChangeAsync(new ChangeEventArgs { Value = "Weekdays" });
        await page.Find(".dv-schedule-form input[type=time]").InputAsync(new ChangeEventArgs { Value = "09:30" });
        await page.Find(".dv-schedule-form textarea").InputAsync(new ChangeEventArgs { Value = "Sample prompt" });
        await page.Find(".dv-schedule-form .dv-button-primary").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-schedule-when").TextContent.Should().Be("Every weekday at 09:30"));
        Server.Sent("POST", $"{bot}/schedules").Should().ContainSingle().Which.Should().Contain("\"cron\":\"30 9 * * 1-5\"").And.Contain("\"threadMode\":\"new_thread\"");
        page.FindAll(".dv-schedule-actions button").Select(button => button.GetAttribute("aria-label")).Should().Equal(
            "Change the schedule Every weekday at 09:30",
            "Remove the schedule Every weekday at 09:30");
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

    #region Helpers

    private static string Words(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<string> Labels(IRenderedComponent<BotEditor> page) =>
        [.. page.FindAll(".dv-disclosure-open label, .dv-disclosure-open legend").Select(label => Words(label.TextContent))];

    private static ModelCatalogResponse Model(string id, bool toolCalling) =>
        new(
            id,
            id,
            new ModelCapabilitiesResponse(null, null, toolCalling, true, false, toolCalling, toolCalling ? ["low", "high"] : [], toolCalling, toolCalling),
            new ModelPriceResponse(null, null, null, "USD"),
            null,
            [],
            "available",
            DateTimeOffset.UnixEpoch);

    private static BotToolEntry TimeTool(string grant, string ceiling, string? parent) =>
        new(toolId, "time.now", "Current time", "Sample description", "builtin", null, null, "read", grant, ceiling, parent, false);

    private static Task Open(IRenderedComponent<BotEditor> page, int section) =>
        page.FindAll(".dv-disclosure-toggle")[section].ClickAsync(new MouseEventArgs());

    private IRenderedComponent<BotEditor> RenderEditor()
    {
        var page = Render<BotEditor>(parameters => parameters.Add(editor => editor.BotId, botId));
        page.WaitForAssertion(() => page.FindAll(".dv-disclosure-toggle").Select(toggle => Words(toggle.TextContent)).Should().Contain("Schedule, None"));
        return page;
    }

    #endregion
}
