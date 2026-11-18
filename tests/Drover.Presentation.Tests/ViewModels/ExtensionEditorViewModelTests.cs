namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The extension editor's view model against a stand-in server: the form, the same file as text, and what is done
/// with the file. Every name and value here is a sample.
/// </summary>
public sealed class ExtensionEditorViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid spaceId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000031");

    private static readonly string root = $"/api/spaces/{spaceId}/extensions";

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    private readonly ExtensionHandoff handoff = new();

    private readonly SessionState session;

    private readonly ServiceProvider services;

    #endregion

    #region Constructors

    public ExtensionEditorViewModelTests()
    {
        session = new(server.CreateApi());
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(server.CreateApi())
            .AddSingleton(session)
            .AddSingleton(navigator)
            .AddSingleton(handoff)
            .BuildServiceProvider();
    }

    #endregion

    #region Properties

    private static ExtensionPackage Package { get; } = new(1, "sample-scribe", "Sample scribe")
    {
        Description = "Sample description",
        Contents = [new BotTemplate("Sample scribe", "Keep notes short.") { Description = "Sample purpose", SuggestedModel = "sample-model" }],
    };

    #endregion

    #region Public Methods

    public void Dispose()
    {
        services.Dispose();
        server.Dispose();
    }

    #endregion

    #region Form

    [Fact]
    public async Task The_editor_opens_on_an_empty_form_with_the_file_as_text_one_tab_away()
    {
        var editor = await Opened();

        ExtensionEditorViewModel.Title.Should().Be("Make an extension");
        ExtensionEditorViewModel.TabsLabel.Should().Be("How the extension is shown");
        ExtensionEditorViewModel.Tabs.Select(tab => tab.Label).Should().Equal("Form", "JSON");
        editor.Tab.Should().Be(ExtensionEditorViewModel.FormTab);
        editor.IsOpen.Should().BeTrue();
        editor.AddsBot.Should().BeTrue("an extension adds a bot until the person says otherwise");
        editor.NamesServer.Should().BeFalse("and names no tool server until the person says so");
        editor.CheckLabel.Should().Be("Check the file");
        ExtensionEditorViewModel.CopyLabel.Should().Be("Copy its text");
        ExtensionEditorViewModel.ImportLabel.Should().Be("Import into this space");
        editor.FileName.Should().Be("extension.json");
        server.Requests.Should().NotContain(request => request.Path.StartsWith(root, StringComparison.Ordinal), "nothing is asked of the server to start from nothing");
    }

    [Fact]
    public async Task The_editor_says_it_is_opening_the_bot_until_it_is_open()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        var held = server.Hold("GET", $"{root}/from-bot/{botId}");
        var editor = Make();

        var opening = editor.Open(botId);

        editor.IsOpen.Should().BeFalse();
        ExtensionEditorViewModel.OpeningNote.Should().Be("Opening the bot.");
        held.SetResult();
        await opening;
    }

    [Fact]
    public async Task The_id_follows_the_name_until_the_person_types_one_of_their_own()
    {
        var editor = await Opened();

        editor.Name = "Sample Note Taker";
        var followed = editor.Id;
        editor.Id = "sample.own-id";
        editor.Name = "Another Name";

        followed.Should().Be("sample-note-taker");
        editor.Id.Should().Be("sample.own-id");
        editor.FileName.Should().Be("sample.own-id.json");
    }

    [Fact]
    public async Task The_form_says_so_when_the_name_changes_the_id_and_when_the_id_is_set_again_to_what_it_is()
    {
        var editor = await Opened();
        var changed = new List<string?>();
        editor.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        editor.Name = "Sample Note Taker";
        editor.Id = editor.Id;

        changed.Should().Equal(nameof(ExtensionEditorViewModel.Name), nameof(ExtensionEditorViewModel.Id));
        editor.Name = "Changed";
        editor.Id.Should().Be("changed", "setting the id to what it already was does not make it the person's own");
    }

    [Fact]
    public async Task Opened_from_a_bot_the_form_holds_that_bots_words()
    {
        server.Answer("GET", $"{root}/from-bot/{botId}", Package);

        var editor = await Opened(botId);

        editor.Name.Should().Be("Sample scribe");
        editor.Id.Should().Be("sample-scribe");
        editor.BotName.Should().Be("Sample scribe");
        editor.BotDescription.Should().Be("Sample purpose");
        editor.Instructions.Should().Be("Keep notes short.");
        editor.SuggestedModel.Should().Be("sample-model");
    }

    [Fact]
    public async Task A_bot_that_cannot_be_opened_says_so_and_can_be_tried_again()
    {
        server.Answer("GET", $"{root}/from-bot/{botId}", HttpStatusCode.ServiceUnavailable);
        var editor = await Opened(botId);

        editor.LoadProblem.Should().NotBeNullOrEmpty();
        editor.IsOpen.Should().BeFalse();
        ExtensionEditorViewModel.LoadFailedTitle.Should().Be("Drover could not open this bot as an extension");

        server.Answer("GET", $"{root}/from-bot/{botId}", Package);
        await editor.LoadCommand.ExecuteAsync(null);

        editor.LoadProblem.Should().BeNull();
        editor.BotName.Should().Be("Sample scribe");
    }

    [Fact]
    public async Task A_member_is_told_who_can_make_an_extension_and_is_given_no_form()
    {
        var editor = await Opened(botId, role: "member");

        editor.CanEdit.Should().BeFalse();
        ExtensionEditorViewModel.MemberNote.Should().Be("An owner or admin of the space can make an extension.");
        server.Requests.Should().NotContain(request => request.Path.StartsWith(root, StringComparison.Ordinal));
    }

    #endregion

    #region Text

    [Fact]
    public async Task The_json_tab_shows_the_form_as_the_text_of_the_file()
    {
        var editor = await Opened();
        editor.Name = "Sample scribe";
        editor.BotName = "Sample scribe";
        editor.Instructions = "Keep notes short.";

        await editor.Show(ExtensionEditorViewModel.JsonTab);

        var written = JsonSerializer.Deserialize<JsonElement>(editor.Text);
        editor.Tab.Should().Be(ExtensionEditorViewModel.JsonTab);
        editor.ShowsJson.Should().BeTrue();
        written.GetProperty("version").GetInt32().Should().Be(1);
        written.GetProperty("id").GetString().Should().Be("sample-scribe");
        written.GetProperty("contents")[0].GetProperty("kind").GetString().Should().Be("bot");
        written.GetProperty("contents")[0].GetProperty("instructions").GetString().Should().Be("Keep notes short.");
        written.GetProperty("contents")[0].TryGetProperty("suggestedModel", out _).Should().BeFalse("a model that was not suggested is left out of the file");
        server.Requests.Should().NotContain(request => request.Method == "POST", "writing the form out asks nothing of the server");
    }

    [Fact]
    public async Task Text_becomes_the_form_only_after_the_server_has_read_it()
    {
        server.Answer("POST", $"{root}/read", Package);
        var editor = await Opened();
        await editor.Show(ExtensionEditorViewModel.JsonTab);

        editor.Text = "sample edited text";
        await editor.Show(ExtensionEditorViewModel.FormTab);

        editor.Tab.Should().Be(ExtensionEditorViewModel.FormTab);
        server.Sent("POST", $"{root}/read").Should().ContainSingle().Which.Should().Contain("sample edited text");
        editor.BotName.Should().Be("Sample scribe");
        editor.SuggestedModel.Should().Be("sample-model");
    }

    [Fact]
    public async Task Text_the_server_cannot_read_stays_on_screen_with_the_reason()
    {
        server.Refuse("POST", $"{root}/read", HttpStatusCode.BadRequest, "This is not an extension file Drover can read. The trouble is at $.grants.");
        var editor = await Opened();
        await editor.Show(ExtensionEditorViewModel.JsonTab);
        editor.Text = "sample broken text";

        await editor.Show(ExtensionEditorViewModel.FormTab);

        editor.Notice.Should().Contain("The trouble is at $.grants.");
        editor.NoticeKind.Should().Be(NoteKind.Danger);
        editor.Tab.Should().Be(ExtensionEditorViewModel.JsonTab);
        editor.Text.Should().Be("sample broken text");
    }

    [Fact]
    public async Task Choosing_the_tab_already_showing_does_nothing_and_clears_the_notice()
    {
        var editor = await Opened();
        editor.Notice = "Sample notice.";

        await editor.Show(ExtensionEditorViewModel.FormTab);

        editor.Notice.Should().BeNull();
        editor.Tab.Should().Be(ExtensionEditorViewModel.FormTab);
    }

    #endregion

    #region Tool Server

    [Fact]
    public async Task Naming_a_tool_server_adds_its_fields_and_writes_it_into_the_file_with_no_field_for_a_key()
    {
        var editor = await Opened();
        editor.Name = "Sample helpdesk";
        editor.BotName = "Sample scribe";

        editor.NamesServer = true;
        editor.ServerName = "Helpdesk";
        editor.ServerAddress = "https://tools.example.test/mcp";
        var beforeHeader = editor.UsesHeader;
        editor.ServerAuth = "header";
        editor.HeaderName = "X-Api-Key";
        editor.SuggestedTools = "lookup: Finds a ticket.\n\nreply\n";
        await editor.Show(ExtensionEditorViewModel.JsonTab);

        var named = JsonSerializer.Deserialize<JsonElement>(editor.Text).GetProperty("contents")[1];
        beforeHeader.Should().BeFalse("a header's name is asked for only when the server uses a header");
        editor.UsesHeader.Should().BeTrue();
        ExtensionEditorViewModel.AuthKinds.Select(kind => kind.Label).Should().Equal("Nothing", "A header", "A sign-in");
        named.EnumerateObject().Select(field => field.Name).Should().Equal("kind", "name", "url", "auth", "headerName", "suggestedTools");
        named.GetProperty("kind").GetString().Should().Be("mcp_connection");
        named.GetProperty("url").GetString().Should().Be("https://tools.example.test/mcp");
        named.GetProperty("auth").GetString().Should().Be("header");
        named.GetProperty("headerName").GetString().Should().Be("X-Api-Key");
        named.GetProperty("suggestedTools").EnumerateArray().Select(tool => (tool.GetProperty("name").GetString(), tool.GetProperty("why").GetString()))
            .Should().Equal(("lookup", "Finds a ticket."), ("reply", string.Empty));
    }

    [Fact]
    public async Task An_extension_that_only_names_a_tool_server_leaves_the_bot_out_of_the_file()
    {
        var editor = await Opened();
        editor.Name = "Sample helpdesk";

        editor.AddsBot = false;
        editor.NamesServer = true;
        editor.ServerName = "Helpdesk";
        editor.ServerAddress = "https://tools.example.test/mcp";
        await editor.Show(ExtensionEditorViewModel.JsonTab);

        var contents = JsonSerializer.Deserialize<JsonElement>(editor.Text).GetProperty("contents");
        contents.GetArrayLength().Should().Be(1);
        contents[0].GetProperty("kind").GetString().Should().Be("mcp_connection");
        contents[0].TryGetProperty("headerName", out _).Should().BeFalse("a server that uses no header names none");
    }

    [Fact]
    public async Task Text_that_names_a_tool_server_fills_its_fields_in_the_form()
    {
        server.Answer("POST", $"{root}/read", Package with
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
        var editor = await Opened();
        await editor.Show(ExtensionEditorViewModel.JsonTab);
        editor.Text = "sample edited text";

        await editor.Show(ExtensionEditorViewModel.FormTab);

        editor.ServerName.Should().Be("Helpdesk");
        editor.AddsBot.Should().BeFalse();
        editor.NamesServer.Should().BeTrue();
        editor.ServerAddress.Should().Be("https://tools.example.test/mcp");
        editor.ServerAuth.Should().Be("oauth", "a sign-in is the way this server is called");
        editor.SuggestedTools.Should().Be("lookup: Finds a ticket.\nreply");
    }

    [Fact]
    public async Task A_form_filled_from_text_says_every_field_changed()
    {
        server.Answer("POST", $"{root}/read", Package);
        var editor = await Opened();
        await editor.Show(ExtensionEditorViewModel.JsonTab);
        var changed = new List<string?>();
        editor.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        await editor.Show(ExtensionEditorViewModel.FormTab);

        changed.Should().Contain(string.Empty, "an empty name means every property changed");
    }

    #endregion

    #region The File

    [Fact]
    public async Task Checking_the_file_has_the_server_read_it_and_says_what_it_found()
    {
        server.Answer("POST", $"{root}/read", Package);
        var editor = await Opened();
        editor.Name = "Sample scribe";

        await editor.CheckCommand.ExecuteAsync(null);

        editor.Notice.Should().Be("This file reads correctly. It can be imported as it is.");
        editor.NoticeKind.Should().Be(NoteKind.Ok);
        server.Sent("POST", $"{root}/read").Should().ContainSingle().Which.Should().Contain("sample-scribe");

        server.Refuse("POST", $"{root}/read", HttpStatusCode.BadRequest, "The bot in this extension needs a name of up to 200 characters.");
        await editor.CheckCommand.ExecuteAsync(null);

        editor.Notice.Should().Contain("The bot in this extension needs a name");
        editor.NoticeKind.Should().Be(NoteKind.Danger);
    }

    [Fact]
    public async Task The_check_button_says_it_is_working_while_the_server_is_asked()
    {
        var held = server.Hold("POST", $"{root}/read");
        var editor = await Opened();

        var checking = editor.CheckCommand.ExecuteAsync(null);

        editor.Busy.Should().BeTrue();
        editor.CheckLabel.Should().Be("Checking");
        held.SetResult();
        await checking;
        editor.CheckLabel.Should().Be("Check the file");
    }

    [Fact]
    public async Task Copying_puts_the_text_of_the_file_on_the_clipboard_and_says_so()
    {
        var editor = await Opened();
        editor.Name = "Sample scribe";
        string? copied = null;

        await editor.Copy(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        editor.Notice.Should().Be("The file's text is copied.");
        editor.NoticeKind.Should().Be(NoteKind.Ok);
        copied.Should().Contain("\"id\": \"sample-scribe\"");
    }

    [Fact]
    public async Task A_clipboard_that_refuses_says_how_to_copy_by_hand()
    {
        var editor = await Opened();

        await editor.Copy(_ => throw new DeviceCallFailedException("Write permission denied."));

        editor.Notice.Should().Be("The text could not be copied. Open the JSON tab, select the text, and copy it by hand.");
        editor.NoticeKind.Should().Be(NoteKind.Danger);
    }

    [Fact]
    public async Task Copying_on_the_json_tab_copies_the_text_being_edited()
    {
        var editor = await Opened();
        await editor.Show(ExtensionEditorViewModel.JsonTab);
        editor.Text = "sample edited text";
        string? copied = null;

        await editor.Copy(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        copied.Should().Be("sample edited text");
    }

    [Fact]
    public async Task Importing_hands_the_file_to_the_extensions_screen_where_it_is_reviewed_first()
    {
        server.Answer("GET", root, Array.Empty<ExtensionSummary>());
        server.Answer("POST", $"{root}/preview", new ExtensionPreview("sample-scribe", "Sample scribe", string.Empty)
        {
            Bot = new ExtensionBotPreview("Sample scribe", string.Empty, "Keep notes short.", 0) { Model = "sample-model", ModelConnection = "Sample connection" },
        });
        var editor = await Opened();
        editor.Name = "Sample scribe";
        editor.Instructions = "Keep notes short.";

        editor.ImportCommand.Execute(null);
        var extensions = ActivatorUtilities.CreateInstance<ExtensionsViewModel>(services);
        await extensions.Open();

        navigator.Current.Should().Be(Addresses.Extensions);
        extensions.ImportTitle.Should().Be("Review Sample scribe");
        server.Sent("POST", $"{root}/preview").Should().ContainSingle().Which.Should().Contain("Keep notes short.");
        server.Requests.Should().NotContain(request => request.Method == "POST" && request.Path == root, "nothing is added before the person says yes on the review");
    }

    #endregion

    #region Private Methods

    private ExtensionEditorViewModel Make() => ActivatorUtilities.CreateInstance<ExtensionEditorViewModel>(services);

    private async Task<ExtensionEditorViewModel> Opened(Guid? bot = null, string role = "owner")
    {
        server.Answer("GET", "/api/session", Samples.Session(role));
        var editor = Make();
        await editor.Open(bot);
        return editor;
    }

    #endregion
}
