namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The extensions screen against a stand-in server: what each state says, what importing sends, and what a person
/// who is not an admin may do. Every name and value here is a sample.
/// </summary>
public sealed class ExtensionsPageTests : ScreenTest
{
    #region Private Fields

    private const string SampleFile = """{"version":1,"id":"sample.note-taker","name":"Sample note taker","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."}]}""";

    private static readonly Guid extensionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000021");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000022");

    private static readonly string root = $"/api/spaces/{SpaceId}/extensions";

    private static ExtensionSummary Installed { get; } =
        new(extensionId, "sample.note-taker", "Sample note taker", "Sample description", DateTimeOffset.UnixEpoch) { BotId = botId, BotName = "Sample scribe", BotStatus = "paused" };

    private static ExtensionPreview Preview { get; } = new("sample.note-taker", "Sample note taker", "Sample description")
    {
        Bot = new ExtensionBotPreview("Sample scribe", "Sample purpose", "Keep notes short.", 0) { Model = "sample-model", ModelConnection = "Sample connection" },
    };

    #endregion

    #region List

    [Fact]
    public void A_space_with_no_extension_says_what_one_is_and_offers_to_import_it()
    {
        Listed();

        var page = Render<ExtensionsPage>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No extension yet"));
        page.Find("h1").TextContent.Should().Be("Extensions");
        page.Find(".dv-empty-cause").TextContent.Should().Contain("the file can give it no tools");
        page.Find(".dv-empty .dv-button").TextContent.Trim().Should().Be("Import an extension");
        page.Find(".dv-extensions-make").Should().Match<AngleSharp.Dom.IElement>(link => link.TextContent == "Make an extension" && link.GetAttribute("href") == "/extensions/edit");
    }

    [Fact]
    public void An_installed_extension_names_its_bot_says_it_is_paused_and_offers_to_turn_it_on()
    {
        Listed(Installed);

        var page = Render<ExtensionsPage>();

        page.WaitForAssertion(() => page.Find(".dv-extension-name").TextContent.Should().Be("Sample note taker"));
        page.Find("ul").GetAttribute("aria-label").Should().Be("Extensions");
        page.Find(".dv-extension-adds").TextContent.Should().Be("Added the bot Sample scribe.");
        page.Find(".dv-extension .dv-badge").TextContent.Should().Be("Paused");
        page.Find(".dv-extension-link").Should().Match<AngleSharp.Dom.IElement>(link => link.TextContent == "Set up Sample scribe" && link.GetAttribute("href") == $"/bots/{botId}");
        page.Find(".dv-extension .dv-button").TextContent.Trim().Should().Be("Turn on Sample scribe");
    }

    [Fact]
    public async Task Turning_a_bot_on_resumes_it_and_says_so()
    {
        Listed(Installed);
        Server.Answer("POST", $"/api/spaces/{SpaceId}/bots/{botId}/resume", Bot("active"));
        var page = Render<ExtensionsPage>();
        page.WaitForElement(".dv-extension .dv-button");

        await page.Find(".dv-extension .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-extension .dv-badge").TextContent.Should().Be("On"));
        page.FindAll(".dv-extension .dv-button").Should().BeEmpty("a bot that is on needs no turning on");
        page.Find(".dv-note-ok").TextContent.Should().Contain("Sample scribe is on.");
    }

    [Fact]
    public void An_extension_whose_bot_was_removed_says_so_and_offers_nothing_for_it()
    {
        Listed(Installed with { BotId = null, BotName = null, BotStatus = null });

        var page = Render<ExtensionsPage>();

        page.WaitForAssertion(() => page.Find(".dv-extension-adds").TextContent.Should().Be("The bot it added has been removed."));
        page.FindAll(".dv-extension .dv-button, .dv-extension-link, .dv-extension .dv-badge").Should().BeEmpty();
    }

    [Fact]
    public void A_member_sees_what_is_installed_and_can_neither_import_nor_turn_a_bot_on()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        Server.Answer("GET", root, new[] { Installed });

        var page = Render<ExtensionsPage>();

        page.WaitForAssertion(() => page.Find(".dv-extension-name").TextContent.Should().Be("Sample note taker"));
        page.FindAll(".dv-button, .dv-extensions-make").Should().BeEmpty();
    }

    [Fact]
    public void A_member_of_a_space_with_no_extension_is_told_who_can_import_one()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        Server.Answer("GET", root, Array.Empty<ExtensionSummary>());

        var page = Render<ExtensionsPage>();

        page.WaitForAssertion(() => page.Find(".dv-extensions-hint").TextContent.Should().Be("This space has no extensions yet. An owner or admin of the space can import one."));
    }

    [Fact]
    public async Task A_list_that_cannot_be_loaded_says_so_and_can_be_tried_again()
    {
        SignedIn();
        Server.Answer("GET", root, HttpStatusCode.ServiceUnavailable);
        var page = Render<ExtensionsPage>();
        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the extensions"));

        Listed(Installed);
        await page.Find(".dv-empty .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-extension-name").TextContent.Should().Be("Sample note taker"));
    }

    #endregion

    #region Import

    [Fact]
    public async Task Importing_reads_the_file_shows_all_of_what_the_bot_will_be_told_and_adds_it_paused()
    {
        Listed();
        Server.Answer("POST", $"{root}/preview", Preview);
        Server.Answer("POST", root, Installed, HttpStatusCode.Created);
        var page = Render<ExtensionsPage>();
        page.WaitForElement(".dv-empty .dv-button");
        await page.Find(".dv-empty .dv-button").ClickAsync(new MouseEventArgs());

        page.Find(".dv-import-title").TextContent.Should().Be("Import an extension");
        page.Find(".dv-import [type=submit]").HasAttribute("disabled").Should().BeTrue("there is nothing to read yet");
        await Type(page, "Or paste its text", SampleFile);
        await page.Find(".dv-import form").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-import-title").TextContent.Should().Be("Review Sample note taker"));
        page.Find(".dv-import").GetAttribute("aria-labelledby").Should().Be(page.Find(".dv-import-title").Id);
        page.FindAll(".dv-import-facts dt").Select(name => name.TextContent).Should().Equal("Adds the bot", "What it is for", "Thinks with", "Tools");
        page.FindAll(".dv-import-facts dd").Select(value => value.TextContent).Should().Equal(
            "Sample scribe",
            "Sample purpose",
            "sample-model on Sample connection. You can change this in the bot's settings.",
            "Only what every new bot starts with. The file cannot give it more.");
        page.Find(".dv-import-text").TextContent.Should().Be("Keep notes short.");
        page.Find(".dv-import-text").GetAttribute("aria-labelledby").Should().Be(page.Find(".dv-import-instructions h3").Id);
        page.Find(".dv-import [type=submit]").TextContent.Trim().Should().Be("Import, paused");

        await page.Find(".dv-import form").SubmitAsync();

        page.WaitForAssertion(() => page.FindAll(".dv-import").Should().BeEmpty());
        Server.Sent("POST", $"{root}/preview").Should().ContainSingle().Which.Should().Contain("sample.note-taker");
        var sent = JsonSerializer.Deserialize<ExtensionText>(Server.Sent("POST", root).Single(), JsonSerializerOptions.Web)!;
        sent.Should().Be(new ExtensionText(SampleFile), "the text that was read is the text that is imported");
        page.Find(".dv-note-ok").TextContent.Should().Contain("Sample note taker is imported. Sample scribe is paused: set it up, then turn it on.");
        page.Find(".dv-extension .dv-badge").TextContent.Should().Be("Paused");
    }

    [Fact]
    public async Task Hidden_characters_in_the_instructions_are_called_out_above_them()
    {
        Listed();
        Server.Answer("POST", $"{root}/preview", Preview with { Bot = Preview.Bot! with { Instructions = "Keep notes short.[U+200B][U+202E]", HiddenCharacters = 2 } });
        var page = Render<ExtensionsPage>();
        await ReadSample(page);

        page.WaitForAssertion(() => page.Find(".dv-import-instructions [role=alert]").TextContent.Should().Contain(
            "These instructions hold 2 characters that do not show on a screen. Each is written out where it stands, like [U+200B]."));
        page.Find(".dv-import-text").TextContent.Should().Be("Keep notes short.[U+200B][U+202E]");
    }

    [Fact]
    public async Task A_file_the_server_refuses_says_why_and_keeps_the_text()
    {
        Listed();
        Server.Refuse("POST", $"{root}/preview", HttpStatusCode.BadRequest, "This is not an extension file Drover can read. The trouble is at $.grants.");
        var page = Render<ExtensionsPage>();
        await ReadSample(page);

        page.WaitForAssertion(() => page.Find(".dv-import [role=alert]").TextContent.Should().Contain("The trouble is at $.grants."));
        page.Find(".dv-import-title").TextContent.Should().Be("Import an extension");
        page.Find(".dv-import textarea").GetAttribute("value").Should().Be(SampleFile);
    }

    [Fact]
    public async Task An_extension_that_is_already_here_says_so_and_cannot_be_imported_again()
    {
        Listed(Installed);
        Server.Answer("POST", $"{root}/preview", Preview with { AlreadyInstalled = true });
        var page = Render<ExtensionsPage>();
        await ReadSample(page);

        page.WaitForAssertion(() => page.Find(".dv-import [role=status]").TextContent.Should().Contain("This extension is already in this space, so it cannot be imported again."));
        page.FindAll(".dv-import [type=submit]").Should().BeEmpty();
        page.FindAll(".dv-import-text").Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_model_connected_the_review_says_what_is_missing_and_links_to_it()
    {
        Listed();
        Server.Answer("POST", $"{root}/preview", Preview with { Bot = Preview.Bot! with { Model = null, ModelConnection = null } });
        var page = Render<ExtensionsPage>();
        await ReadSample(page);

        page.WaitForAssertion(() => page.Find(".dv-import-hint").TextContent.Should().Be(
            "A bot needs a model to think with, and this space has none it can use yet. Connect a model, then import this."));
        page.FindAll(".dv-import-facts dt").Select(name => name.TextContent).Should().NotContain("Thinks with");
        page.Find(".dv-import-hint a").GetAttribute("href").Should().Be("/models");
        page.FindAll(".dv-import [type=submit]").Should().BeEmpty();
    }

    [Fact]
    public async Task An_import_the_server_refuses_says_why_and_stays_on_the_review()
    {
        Listed();
        Server.Answer("POST", $"{root}/preview", Preview);
        Server.Refuse("POST", root, HttpStatusCode.BadRequest, "This space allows at most 100 bots, and already has 100. Remove a bot to make room for another.");
        var page = Render<ExtensionsPage>();
        await ReadSample(page);
        page.WaitForElement(".dv-import-text");

        await page.Find(".dv-import form").SubmitAsync();

        page.WaitForAssertion(() => page.FindAll(".dv-import [role=alert]").Select(alert => alert.TextContent.Trim()).Should().Contain(
            "This space allows at most 100 bots, and already has 100. Remove a bot to make room for another."));
        page.Find(".dv-import-title").TextContent.Should().Be("Review Sample note taker");
    }

    [Fact]
    public async Task Cancel_closes_the_import_and_adds_nothing()
    {
        Listed();
        var page = Render<ExtensionsPage>();
        page.WaitForElement(".dv-empty .dv-button");
        await page.Find(".dv-empty .dv-button").ClickAsync(new MouseEventArgs());

        await page.FindAll(".dv-import-actions .dv-button")[0].ClickAsync(new MouseEventArgs());

        page.FindAll(".dv-import").Should().BeEmpty();
        Server.Requests.Should().NotContain(request => request.Method == "POST");
    }

    #endregion

    #region Helpers

    private void Listed(params ExtensionSummary[] extensions)
    {
        SignedIn();
        Server.Answer("GET", root, extensions);
    }

    private static BotDetail Bot(string status) =>
        new(
            botId, "Sample scribe", "sample-scribe", new BotAvatarBody(null, null), "Sample purpose", "Keep notes short.",
            Guid.Parse("0196f1a0-0000-7000-8000-000000000023"), "sample-default", new ModelSettingsBody(null, null, null, "off", false),
            new MemorySettingsBody(true, "none", null, null, 300, 4000), "none", new SandboxSettingsBody("off", [], null), [],
            status, null, 0, new BuildLimitsBody(null, null), new RunLimitsBody(null, null, null), null, null);

    /// <summary>Opens the import, pastes the sample file, and asks the server to read it.</summary>
    private static async Task ReadSample(IRenderedComponent<ExtensionsPage> page)
    {
        page.WaitForElement(".dv-button");
        await page.Find(".dv-button").ClickAsync(new MouseEventArgs());
        await Type(page, "Or paste its text", SampleFile);
        await page.Find(".dv-import form").SubmitAsync();
    }

    private static Task Type(IRenderedComponent<ExtensionsPage> page, string label, string text)
    {
        var id = page.FindAll(".dv-import label").First(found => found.TextContent.Trim() == label).GetAttribute("for");
        return page.Find($"#{id}").InputAsync(new ChangeEventArgs { Value = text });
    }

    #endregion
}
