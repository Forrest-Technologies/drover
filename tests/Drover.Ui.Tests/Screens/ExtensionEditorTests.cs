namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The extension editor against a stand-in server: the form, the same file as text, and what is done with the
/// file. Every name and value here is a sample.
/// </summary>
public sealed class ExtensionEditorTests : ScreenTest
{
    #region Private Fields

    private const string ClipboardModule = "./_content/Drover.Ui/Components/CodeBlock.razor.js";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000031");

    private static readonly string root = $"/api/spaces/{SpaceId}/extensions";

    private static ExtensionPackage Package { get; } = new(1, "sample-scribe", "Sample scribe")
    {
        Description = "Sample description",
        Contents = [new BotTemplate("Sample scribe", "Keep notes short.") { Description = "Sample purpose", SuggestedModel = "sample-model" }],
    };

    #endregion

    #region Form

    [Fact]
    public void The_editor_opens_on_an_empty_form_with_the_file_as_text_one_tab_away()
    {
        SignedIn();

        var page = Render<ExtensionEditor>();

        page.WaitForAssertion(() => page.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).Should().Equal("Form", "JSON"));
        page.Find("h1").TextContent.Should().Be("Make an extension");
        page.Find("[role=tablist]").GetAttribute("aria-label").Should().Be("How the extension is shown");
        page.Find("[role=tab][aria-selected=true]").TextContent.Trim().Should().Be("Form");
        page.FindAll(".dv-extension-editor label").Select(label => label.TextContent.Trim()).Should().Equal(
            "Extension name", "Id", "What the extension is for", "Adds a bot", "Bot name", "What the bot is for", "Instructions", "Suggested model", "Names a tool server");
        page.FindAll(".dv-extension-editor-part").Select(part => part.TextContent).Should().Equal("The bot it adds", "The tool server it names");
        page.FindAll("[role=switch]").Select(toggle => toggle.HasAttribute("checked")).Should().Equal([true, false], "an extension adds a bot and names no tool server until the person says so");
        page.FindAll(".dv-extension-editor-actions .dv-button").Select(button => button.TextContent.Trim()).Should().Equal(
            "Check the file", "Copy its text", "Import into this space");
        page.Find(".dv-extension-editor-download").Should().Match<AngleSharp.Dom.IElement>(
            link => link.TextContent == "Download extension.json" && link.GetAttribute("download") == "extension.json");
        Server.Requests.Should().NotContain(request => request.Path.StartsWith(root, StringComparison.Ordinal), "nothing is asked of the server to start from nothing");
    }

    [Fact]
    public async Task The_id_follows_the_name_until_the_person_types_one_of_their_own()
    {
        SignedIn();
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");

        await Type(page, "Extension name", "Sample Note Taker");
        var followed = Value(page, "Id");
        await Type(page, "Id", "sample.own-id");
        await Type(page, "Extension name", "Another Name");

        followed.Should().Be("sample-note-taker");
        Value(page, "Id").Should().Be("sample.own-id");
        page.Find(".dv-extension-editor-download").GetAttribute("download").Should().Be("sample.own-id.json");
    }

    [Fact]
    public void Opened_from_a_bot_the_form_holds_that_bots_words()
    {
        SignedIn();
        Server.Answer("GET", $"{root}/from-bot/{botId}", Package);
        Navigation.NavigateTo(Addresses.ExtensionFromBot(botId));

        var page = Render<ExtensionEditor>();

        page.WaitForAssertion(() => Value(page, "Extension name").Should().Be("Sample scribe"));
        Value(page, "Id").Should().Be("sample-scribe");
        Value(page, "Bot name").Should().Be("Sample scribe");
        Value(page, "What the bot is for").Should().Be("Sample purpose");
        Value(page, "Instructions").Should().Be("Keep notes short.");
        Value(page, "Suggested model").Should().Be("sample-model");
    }

    [Fact]
    public async Task A_bot_that_cannot_be_opened_says_so_and_can_be_tried_again()
    {
        SignedIn();
        Server.Answer("GET", $"{root}/from-bot/{botId}", HttpStatusCode.ServiceUnavailable);
        Navigation.NavigateTo(Addresses.ExtensionFromBot(botId));
        var page = Render<ExtensionEditor>();
        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not open this bot as an extension"));

        Server.Answer("GET", $"{root}/from-bot/{botId}", Package);
        await page.Find(".dv-empty .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Value(page, "Bot name").Should().Be("Sample scribe"));
    }

    [Fact]
    public void A_member_is_told_who_can_make_an_extension_and_is_given_no_form()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        Navigation.NavigateTo(Addresses.ExtensionFromBot(botId));

        var page = Render<ExtensionEditor>();

        page.WaitForAssertion(() => page.Find(".dv-extension-editor-hint").TextContent.Should().Be("An owner or admin of the space can make an extension."));
        page.FindAll(".dv-extension-editor, .dv-button").Should().BeEmpty();
        Server.Requests.Should().NotContain(request => request.Path.StartsWith(root, StringComparison.Ordinal));
    }

    #endregion

    #region Text

    [Fact]
    public async Task The_json_tab_shows_the_form_as_the_text_of_the_file()
    {
        SignedIn();
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await Type(page, "Extension name", "Sample scribe");
        await Type(page, "Bot name", "Sample scribe");
        await Type(page, "Instructions", "Keep notes short.");

        await page.FindAll("[role=tab]")[1].ClickAsync(new MouseEventArgs());

        var written = JsonSerializer.Deserialize<JsonElement>(Value(page, "The file as text")!);
        page.Find("[role=tab][aria-selected=true]").TextContent.Trim().Should().Be("JSON");
        written.GetProperty("version").GetInt32().Should().Be(1);
        written.GetProperty("id").GetString().Should().Be("sample-scribe");
        written.GetProperty("contents")[0].GetProperty("kind").GetString().Should().Be("bot");
        written.GetProperty("contents")[0].GetProperty("instructions").GetString().Should().Be("Keep notes short.");
        written.GetProperty("contents")[0].TryGetProperty("suggestedModel", out _).Should().BeFalse("a model that was not suggested is left out of the file");
        Server.Requests.Should().NotContain(request => request.Method == "POST", "writing the form out asks nothing of the server");
    }

    [Fact]
    public async Task Text_becomes_the_form_only_after_the_server_has_read_it()
    {
        SignedIn();
        Server.Answer("POST", $"{root}/read", Package);
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await page.FindAll("[role=tab]")[1].ClickAsync(new MouseEventArgs());

        await Type(page, "The file as text", "sample edited text");
        await page.FindAll("[role=tab]")[0].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find("[role=tab][aria-selected=true]").TextContent.Trim().Should().Be("Form"));
        Server.Sent("POST", $"{root}/read").Should().ContainSingle().Which.Should().Contain("sample edited text");
        Value(page, "Bot name").Should().Be("Sample scribe");
        Value(page, "Suggested model").Should().Be("sample-model");
    }

    [Fact]
    public async Task Text_the_server_cannot_read_stays_on_screen_with_the_reason()
    {
        SignedIn();
        Server.Refuse("POST", $"{root}/read", HttpStatusCode.BadRequest, "This is not an extension file Drover can read. The trouble is at $.grants.");
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await page.FindAll("[role=tab]")[1].ClickAsync(new MouseEventArgs());
        await Type(page, "The file as text", "sample broken text");

        await page.FindAll("[role=tab]")[0].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-extension-editor [role=alert]").TextContent.Should().Contain("The trouble is at $.grants."));
        page.Find("[role=tab][aria-selected=true]").TextContent.Trim().Should().Be("JSON");
        Value(page, "The file as text").Should().Be("sample broken text");
    }

    #endregion

    #region Tool Server

    [Fact]
    public async Task Naming_a_tool_server_adds_its_fields_and_writes_it_into_the_file_with_no_field_for_a_key()
    {
        SignedIn();
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await Type(page, "Extension name", "Sample helpdesk");
        await Type(page, "Bot name", "Sample scribe");

        await page.FindAll("[role=switch]")[1].ChangeAsync(new ChangeEventArgs { Value = true });
        await Type(page, "Server name", "Helpdesk");
        await Type(page, "Server address", "https://tools.example.test/mcp");
        var beforeHeader = page.FindAll(".dv-extension-editor label").Select(label => label.TextContent.Trim()).ToList();
        await page.FindAll(".dv-segment-input")[1].ChangeAsync(new ChangeEventArgs { Value = "on" });
        await Type(page, "Header name", "X-Api-Key");
        await Type(page, "Tools to suggest for the bot", "lookup: Finds a ticket.\n\nreply\n");
        await page.FindAll("[role=tab]")[1].ClickAsync(new MouseEventArgs());

        var server = JsonSerializer.Deserialize<JsonElement>(Value(page, "The file as text")!).GetProperty("contents")[1];
        beforeHeader.Should().NotContain("Header name", "a header's name is asked for only when the server uses a header");
        server.EnumerateObject().Select(field => field.Name).Should().Equal("kind", "name", "url", "auth", "headerName", "suggestedTools");
        server.GetProperty("kind").GetString().Should().Be("mcp_connection");
        server.GetProperty("url").GetString().Should().Be("https://tools.example.test/mcp");
        server.GetProperty("auth").GetString().Should().Be("header");
        server.GetProperty("headerName").GetString().Should().Be("X-Api-Key");
        server.GetProperty("suggestedTools").EnumerateArray().Select(tool => (tool.GetProperty("name").GetString(), tool.GetProperty("why").GetString()))
            .Should().Equal(("lookup", "Finds a ticket."), ("reply", string.Empty));
    }

    [Fact]
    public async Task An_extension_that_only_names_a_tool_server_leaves_the_bot_out_of_the_file()
    {
        SignedIn();
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await Type(page, "Extension name", "Sample helpdesk");

        await page.FindAll("[role=switch]")[0].ChangeAsync(new ChangeEventArgs { Value = false });
        await page.FindAll("[role=switch]")[1].ChangeAsync(new ChangeEventArgs { Value = true });
        await Type(page, "Server name", "Helpdesk");
        await Type(page, "Server address", "https://tools.example.test/mcp");
        var labels = page.FindAll(".dv-extension-editor label").Select(label => label.TextContent.Trim()).ToList();
        await page.FindAll("[role=tab]")[1].ClickAsync(new MouseEventArgs());

        var contents = JsonSerializer.Deserialize<JsonElement>(Value(page, "The file as text")!).GetProperty("contents");
        labels.Should().NotContain("Bot name");
        contents.GetArrayLength().Should().Be(1);
        contents[0].GetProperty("kind").GetString().Should().Be("mcp_connection");
        contents[0].TryGetProperty("headerName", out _).Should().BeFalse("a server that uses no header names none");
    }

    [Fact]
    public async Task Text_that_names_a_tool_server_fills_its_fields_in_the_form()
    {
        SignedIn();
        Server.Answer("POST", $"{root}/read", Package with
        {
            Contents =
            [
                new ConnectionRecipe("Helpdesk", "https://tools.example.test/mcp")
                {
                    Auth = "oauth",
                    SuggestedTools = [new SuggestedTool("lookup", "Finds a ticket."), new SuggestedTool("reply")],
                },
            ],
        });
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await page.FindAll("[role=tab]")[1].ClickAsync(new MouseEventArgs());
        await Type(page, "The file as text", "sample edited text");

        await page.FindAll("[role=tab]")[0].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Value(page, "Server name").Should().Be("Helpdesk"));
        page.FindAll("[role=switch]").Select(toggle => toggle.HasAttribute("checked")).Should().Equal([false, true]);
        Value(page, "Server address").Should().Be("https://tools.example.test/mcp");
        page.FindAll(".dv-segment-input")[2].HasAttribute("checked").Should().BeTrue("a sign-in is the way this server is called");
        Value(page, "Tools to suggest for the bot").Should().Be("lookup: Finds a ticket.\nreply");
    }

    #endregion

    #region The File

    [Fact]
    public async Task Checking_the_file_has_the_server_read_it_and_says_what_it_found()
    {
        SignedIn();
        Server.Answer("POST", $"{root}/read", Package);
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await Type(page, "Extension name", "Sample scribe");

        await page.FindAll(".dv-extension-editor-actions .dv-button")[0].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-note-ok").TextContent.Should().Contain("This file reads correctly. It can be imported as it is."));
        Server.Sent("POST", $"{root}/read").Should().ContainSingle().Which.Should().Contain("sample-scribe");

        Server.Refuse("POST", $"{root}/read", HttpStatusCode.BadRequest, "The bot in this extension needs a name of up to 200 characters.");
        await page.FindAll(".dv-extension-editor-actions .dv-button")[0].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-extension-editor [role=alert]").TextContent.Should().Contain("The bot in this extension needs a name"));
    }

    [Fact]
    public async Task Copying_puts_the_text_of_the_file_on_the_clipboard_and_says_so()
    {
        SignedIn();
        var module = JSInterop.SetupModule(ClipboardModule);
        module.SetupVoid("copyText", _ => true).SetVoidResult();
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");
        await Type(page, "Extension name", "Sample scribe");

        await page.FindAll(".dv-extension-editor-actions .dv-button")[1].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-note-ok").TextContent.Should().Contain("The file's text is copied."));
        module.VerifyInvoke("copyText").Arguments.Single().Should().BeOfType<string>().Which.Should().Contain("\"id\": \"sample-scribe\"");
    }

    [Fact]
    public async Task A_clipboard_that_refuses_says_how_to_copy_by_hand()
    {
        SignedIn();
        JSInterop.SetupModule(ClipboardModule).SetupVoid("copyText", _ => true).SetException(new JSException("Write permission denied."));
        var page = Render<ExtensionEditor>();
        page.WaitForElement(".dv-extension-editor");

        await page.FindAll(".dv-extension-editor-actions .dv-button")[1].ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-extension-editor [role=alert]").TextContent.Should().Contain(
            "The text could not be copied. Open the JSON tab, select the text, and copy it by hand."));
    }

    [Fact]
    public async Task Importing_hands_the_file_to_the_extensions_screen_where_it_is_reviewed_first()
    {
        SignedIn();
        Server.Answer("GET", root, Array.Empty<ExtensionSummary>());
        Server.Answer("POST", $"{root}/preview", new ExtensionPreview("sample-scribe", "Sample scribe", string.Empty)
        {
            Bot = new ExtensionBotPreview("Sample scribe", string.Empty, "Keep notes short.", 0) { Model = "sample-model", ModelConnection = "Sample connection" },
        });
        var editor = Render<ExtensionEditor>();
        editor.WaitForElement(".dv-extension-editor");
        await Type(editor, "Extension name", "Sample scribe");
        await Type(editor, "Instructions", "Keep notes short.");

        await editor.Find(".dv-extension-editor-actions .dv-button-primary").ClickAsync(new MouseEventArgs());
        var extensions = Render<ExtensionsPage>();

        Address.Should().Be("extensions");
        extensions.WaitForAssertion(() => extensions.Find(".dv-import-title").TextContent.Should().Be("Review Sample scribe"));
        Server.Sent("POST", $"{root}/preview").Should().ContainSingle().Which.Should().Contain("Keep notes short.");
        Server.Requests.Should().NotContain(request => request.Method == "POST" && request.Path == root, "nothing is added before the person says yes on the review");
    }

    [Fact]
    public void In_the_native_app_there_is_no_download_and_the_file_is_copied_or_imported()
    {
        SignedIn();
        Services.AddSingleton<IFileDelivery>(new NoDelivery());

        var page = Render<ExtensionEditor>();

        page.WaitForElement(".dv-extension-editor");
        page.FindAll(".dv-extension-editor-download").Should().BeEmpty();
        page.FindAll(".dv-extension-editor-actions .dv-button").Select(button => button.TextContent.Trim()).Should().Equal(
            "Check the file", "Copy its text", "Import into this space");
    }

    #endregion

    #region Helpers

    private static string? Value(IRenderedComponent<ExtensionEditor> page, string label) =>
        page.Find($"#{IdOf(page, label)}").GetAttribute("value");

    private static Task Type(IRenderedComponent<ExtensionEditor> page, string label, string text) =>
        page.Find($"#{IdOf(page, label)}").InputAsync(new ChangeEventArgs { Value = text });

    private static string IdOf(IRenderedComponent<ExtensionEditor> page, string label) =>
        page.FindAll(".dv-extension-editor label").First(found => found.TextContent.Trim() == label).GetAttribute("for")!;

    private sealed class NoDelivery : IFileDelivery
    {
        public Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task Share(SharedFile file, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    #endregion
}
