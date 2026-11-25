namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The extensions screen's view model against a stand-in server: what each state says, what importing sends, and what
/// a person who is not an admin may do. Every name and value here is a sample.
/// </summary>
public sealed class ExtensionsViewModelTests : IDisposable
{
    #region Private Fields

    private const string SampleFile = """{"version":1,"id":"sample.note-taker","name":"Sample note taker","contents":[{"kind":"bot","name":"Sample scribe","instructions":"Keep notes short."}]}""";

    private static readonly Guid spaceId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid extensionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000021");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000022");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000024");

    private static readonly string root = $"/api/spaces/{spaceId}/extensions";

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    private readonly ExtensionHandoff handoff = new();

    private readonly SessionState session;

    private readonly ServiceProvider services;

    #endregion

    #region Constructors

    public ExtensionsViewModelTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
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

    private static ExtensionSummary Installed { get; } =
        new(extensionId, "sample.note-taker", "Sample note taker", "Sample description", DateTimeOffset.UnixEpoch)
        {
            BotId = botId,
            BotName = "Sample scribe",
            BotStatus = "paused",
        };

    private static ExtensionPreview Preview { get; } = new("sample.note-taker", "Sample note taker", "Sample description")
    {
        Bot = new ExtensionBotPreview("Sample scribe", "Sample purpose", "Keep notes short.", 0)
        {
            Model = "sample-model",
            ModelConnection = "Sample connection",
        },
    };

    private static ExtensionConnectionPreview ServerPreview { get; } =
        new("Helpdesk", "https://tools.example.test/mcp", "tools.example.test", "header", "X-Api-Key", IsLocal: false, Available: true)
        {
            SuggestedTools = [new SuggestedTool("lookup", "Finds a ticket."), new SuggestedTool("reply")],
        };

    private static ExtensionConnectionSummary ServerState { get; } =
        new("Helpdesk", "https://tools.example.test/mcp", "tools.example.test", "header", "X-Api-Key", IsLocal: false, Available: true)
        {
            Tools = [new SuggestedToolState("lookup", "Finds a ticket.", null, "off"), new SuggestedToolState("reply", string.Empty, null, "off")],
        };

    private static ExtensionConnectionSummary Connected { get; } = ServerState with
    {
        ConnectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000025"),
        Status = "ready",
        Tools = [new SuggestedToolState("lookup", "Finds a ticket.", toolId, "off"), new SuggestedToolState("reply", string.Empty, null, "off")],
    };

    #endregion

    #region Public Methods

    public void Dispose()
    {
        services.Dispose();
        server.Dispose();
    }

    #endregion

    #region List

    [Fact]
    public async Task A_space_with_no_extension_says_what_one_is_and_offers_to_import_it()
    {
        var screen = await Opened();

        screen.Shows.Should().Be(ExtensionsContent.Empty);
        ExtensionsViewModel.Title.Should().Be("Extensions");
        ExtensionsViewModel.EmptyTitle.Should().Be("No extension yet");
        ExtensionsViewModel.EmptyCause.Should().Contain("the file can give the bot no tools");
        screen.ShowsActions.Should().BeTrue("the make link is there, and the import button is the empty state's own action");
        screen.ShowsImportButton.Should().BeFalse();
    }

    [Fact]
    public async Task The_list_says_it_is_loading_until_the_server_has_answered()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        var held = server.Hold("GET", root);
        var screen = Make();

        var opening = screen.Open();

        screen.Shows.Should().Be(ExtensionsContent.Loading);
        ExtensionsViewModel.LoadingNote.Should().Be("Loading the extensions.");
        screen.ShowsActions.Should().BeFalse();
        held.SetResult();
        await opening;
    }

    [Fact]
    public async Task An_installed_extension_names_its_bot_says_it_is_paused_and_offers_to_turn_it_on()
    {
        var screen = await Opened(Installed);

        screen.Shows.Should().Be(ExtensionsContent.List);
        screen.ShowsImportButton.Should().BeTrue();
        ExtensionWords.Adds(Installed).Should().Be("Added the bot Sample scribe.");
        ExtensionWords.Status(Installed).Should().Be("Paused");
        ExtensionsViewModel.StatusKind(Installed).Should().Be(BadgeKind.Neutral);
        ExtensionsViewModel.SetUpLabel(Installed).Should().Be("Set up Sample scribe");
        Addresses.Bot(botId).Should().Be($"/bots/{botId}");
        screen.ShowsTurnOn(Installed).Should().BeTrue();
        screen.TurnOnLabel(Installed).Should().Be("Turn on Sample scribe");
    }

    [Fact]
    public async Task Turning_a_bot_on_resumes_it_and_says_so()
    {
        server.Answer("POST", $"/api/spaces/{spaceId}/bots/{botId}/resume", Bot("active"));
        var screen = await Opened(Installed);

        await screen.TurnOn(Installed);

        var now = screen.Extensions!.Single();
        ExtensionWords.Status(now).Should().Be("On");
        ExtensionsViewModel.StatusKind(now).Should().Be(BadgeKind.Ok);
        screen.ShowsTurnOn(now).Should().BeFalse("a bot that is on needs no turning on");
        screen.Notice.Should().Contain("Sample scribe is on.");
        screen.NoticeKind.Should().Be(NoteKind.Ok);
        screen.Busy.Should().BeNull();
    }

    [Fact]
    public async Task The_turn_on_button_says_it_is_working_while_the_server_is_asked()
    {
        var held = server.Hold("POST", $"/api/spaces/{spaceId}/bots/{botId}/resume");
        var screen = await Opened(Installed);

        var turning = screen.TurnOn(Installed);

        screen.TurnOnLabel(Installed).Should().Be("Turning on");
        held.SetResult();
        await turning;
    }

    [Fact]
    public async Task A_bot_the_server_will_not_turn_on_says_why()
    {
        server.Refuse("POST", $"/api/spaces/{spaceId}/bots/{botId}/resume", HttpStatusCode.BadRequest, "Sample refusal.");
        var screen = await Opened(Installed);

        await screen.TurnOn(Installed);

        screen.Notice.Should().Contain("Sample refusal.");
        screen.NoticeKind.Should().Be(NoteKind.Danger);
        ExtensionWords.Status(screen.Extensions!.Single()).Should().Be("Paused");
    }

    [Fact]
    public async Task An_extension_whose_bot_was_removed_says_so_and_offers_nothing_for_it()
    {
        var removed = Installed with { BotId = null, BotName = null, BotStatus = null };
        var screen = await Opened(removed);

        ExtensionWords.Adds(removed).Should().Be("The bot it added has been removed.");
        screen.ShowsTurnOn(removed).Should().BeFalse();
    }

    [Fact]
    public async Task A_member_sees_what_is_installed_and_can_neither_import_nor_turn_a_bot_on()
    {
        var screen = await Opened(role: "member", extensions: Installed);

        screen.Shows.Should().Be(ExtensionsContent.List);
        screen.CanEdit.Should().BeFalse();
        screen.ShowsActions.Should().BeFalse("a member has no make link and no import button");
        screen.ShowsTurnOn(Installed).Should().BeFalse();
    }

    [Fact]
    public async Task A_member_of_a_space_with_no_extension_is_told_who_can_import_one()
    {
        var screen = await Opened(role: "member");

        screen.Shows.Should().Be(ExtensionsContent.EmptyForMember);
        ExtensionsViewModel.EmptyForMember.Should().Be("This space has no extensions yet. An owner or admin of the space can import one.");
    }

    [Fact]
    public async Task A_list_that_cannot_be_loaded_says_so_and_can_be_tried_again()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        server.Answer("GET", root, HttpStatusCode.ServiceUnavailable);
        var screen = Make();
        await screen.Open();

        screen.Shows.Should().Be(ExtensionsContent.LoadFailed);
        ExtensionsViewModel.LoadFailedTitle.Should().Be("Drover could not load the extensions");
        screen.LoadProblem.Should().NotBeNullOrEmpty();

        server.Answer("GET", root, new[] { Installed });
        await screen.LoadCommand.ExecuteAsync(null);

        screen.Shows.Should().Be(ExtensionsContent.List);
        screen.LoadProblem.Should().BeNull();
    }

    #endregion

    #region Import

    [Fact]
    public async Task Importing_reads_the_file_shows_all_of_what_the_bot_will_be_told_and_adds_it_paused()
    {
        server.Answer("POST", $"{root}/preview", Preview);
        server.Answer("POST", root, Installed, HttpStatusCode.Created);
        var screen = await Opened();

        screen.StartImporting();

        screen.Importing.Should().BeTrue();
        screen.Shows.Should().Be(ExtensionsContent.Empty, "the empty state stays behind the import sheet");
        screen.ImportTitle.Should().Be("Import an extension");
        screen.Form.CanRead.Should().BeFalse("there is nothing to read yet");
        screen.Form.Text = SampleFile;
        screen.Form.CanRead.Should().BeTrue();
        await screen.ReadFormCommand.ExecuteAsync(null);

        screen.ImportTitle.Should().Be("Review Sample note taker");
        screen.Preview!.Bot!.Instructions.Should().Be("Keep notes short.");
        screen.CanImport.Should().BeTrue();
        screen.ImportLabel.Should().Be("Import, paused");

        await screen.ImportCommand.ExecuteAsync(null);

        screen.Importing.Should().BeFalse();
        server.Sent("POST", $"{root}/preview").Should().ContainSingle().Which.Should().Contain("sample.note-taker");
        var sent = JsonSerializer.Deserialize<ExtensionText>(server.Sent("POST", root).Single(), JsonSerializerOptions.Web)!;
        sent.Should().Be(new ExtensionText(SampleFile), "the text that was read is the text that is imported");
        screen.Notice.Should().Contain("Sample note taker is imported. Sample scribe is paused: set it up, then turn it on.");
        ExtensionWords.Status(screen.Extensions!.Single()).Should().Be("Paused");
    }

    [Fact]
    public async Task The_import_buttons_say_they_are_working_while_the_server_is_asked()
    {
        var held = server.Hold("POST", $"{root}/preview");
        var screen = await Opened();
        screen.StartImporting();
        screen.Form.Text = SampleFile;

        var reading = screen.ReadFormCommand.ExecuteAsync(null);

        screen.ImportBusy.Should().BeTrue();
        screen.ReadLabel.Should().Be("Reading the file");
        screen.ImportLabel.Should().Be("Importing");
        held.SetResult();
        await reading;
        screen.ReadLabel.Should().Be("Read the file");
    }

    [Fact]
    public async Task Hidden_characters_in_the_instructions_are_called_out_above_them()
    {
        server.Answer("POST", $"{root}/preview", Preview with { Bot = Preview.Bot! with { Instructions = "Keep notes short.[U+200B][U+202E]", HiddenCharacters = 2 } });
        var screen = await Opened();

        await ReadSample(screen);

        screen.Preview!.Bot!.HiddenCharacters.Should().Be(2);
        ExtensionWords.Hidden(2).Should().Be(
            "These instructions hold 2 characters that do not show on a screen. Each is written out where it stands, like [U+200B].");
        screen.Preview.Bot.Instructions.Should().Be("Keep notes short.[U+200B][U+202E]");
    }

    [Fact]
    public async Task A_file_the_server_refuses_says_why_and_keeps_the_text()
    {
        server.Refuse("POST", $"{root}/preview", HttpStatusCode.BadRequest, "This is not an extension file Drover can read. The trouble is at $.grants.");
        var screen = await Opened();

        await ReadSample(screen);

        screen.ImportProblem.Should().Contain("The trouble is at $.grants.");
        screen.ImportTitle.Should().Be("Import an extension");
        screen.Form.Text.Should().Be(SampleFile);
    }

    [Fact]
    public async Task An_extension_that_is_already_here_says_so_and_cannot_be_imported_again()
    {
        server.Answer("POST", $"{root}/preview", Preview with { AlreadyInstalled = true });
        var screen = await Opened(Installed);

        await ReadSample(screen);

        screen.Preview!.AlreadyInstalled.Should().BeTrue();
        screen.CanImport.Should().BeFalse();
    }

    [Fact]
    public async Task With_no_model_connected_the_review_says_what_is_missing_and_offers_no_import()
    {
        server.Answer("POST", $"{root}/preview", Preview with { Bot = Preview.Bot! with { Model = null, ModelConnection = null } });
        var screen = await Opened();

        await ReadSample(screen);

        screen.Preview!.Bot!.Model.Should().BeNull();
        screen.CanImport.Should().BeFalse();
        Addresses.Models.Should().Be("/models");
    }

    [Fact]
    public async Task A_file_that_only_names_a_tool_server_can_be_imported_with_no_bot_to_think()
    {
        server.Answer("POST", $"{root}/preview", new ExtensionPreview("sample.helpdesk", "Sample helpdesk", string.Empty) { Connection = ServerPreview });
        var screen = await Opened();

        await ReadSample(screen);

        screen.CanImport.Should().BeTrue("there is no bot, so no model is needed");
    }

    [Fact]
    public async Task An_import_the_server_refuses_says_why_and_stays_on_the_review()
    {
        server.Answer("POST", $"{root}/preview", Preview);
        server.Refuse("POST", root, HttpStatusCode.BadRequest, "This space allows at most 100 bots, and already has 100. Remove a bot to make room for another.");
        var screen = await Opened();
        await ReadSample(screen);

        await screen.ImportCommand.ExecuteAsync(null);

        screen.ImportProblem.Should().Contain("This space allows at most 100 bots, and already has 100. Remove a bot to make room for another.");
        screen.ImportTitle.Should().Be("Review Sample note taker");
        screen.Importing.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_closes_the_import_and_adds_nothing()
    {
        var screen = await Opened();
        screen.StartImporting();

        screen.StopImportingCommand.Execute(null);

        screen.Importing.Should().BeFalse();
        server.Requests.Should().NotContain(request => request.Method == "POST");
    }

    [Fact]
    public async Task Starting_again_after_a_review_begins_with_nothing_read()
    {
        server.Answer("POST", $"{root}/preview", Preview);
        var screen = await Opened();
        await ReadSample(screen);

        screen.StopImporting();
        screen.StartImporting();

        screen.Preview.Should().BeNull();
        screen.Form.Text.Should().BeEmpty();
        screen.ImportProblem.Should().BeNull();
    }

    [Fact]
    public async Task A_chosen_file_goes_into_the_text_the_same_way_pasted_text_does()
    {
        var screen = await Opened();
        screen.StartImporting();

        await screen.Form.Choose(SampleFile.Length, () => Task.FromResult(SampleFile));

        screen.Form.Text.Should().Be(SampleFile);
        screen.Form.FileProblem.Should().BeNull();
    }

    [Fact]
    public async Task A_file_larger_than_an_extension_can_be_is_not_opened()
    {
        var screen = await Opened();
        screen.StartImporting();
        var opened = false;

        await screen.Form.Choose(256 * 1024 + 1, () =>
        {
            opened = true;
            return Task.FromResult(string.Empty);
        });

        opened.Should().BeFalse();
        screen.Form.FileProblem.Should().Be("This file is larger than an extension can be (256 KB).");
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_says_so()
    {
        var screen = await Opened();
        screen.StartImporting();

        await screen.Form.Choose(10, () => throw new IOException("Sample failure."));

        screen.Form.FileProblem.Should().Be("The file could not be read. Choose it again, or paste its text.");
    }

    [Fact]
    public async Task A_file_handed_over_by_the_editor_is_reviewed_here_before_anything_is_added()
    {
        server.Answer("POST", $"{root}/preview", Preview);
        handoff.Give(SampleFile);

        var screen = await Opened();

        screen.Importing.Should().BeTrue();
        screen.Form.Text.Should().Be(SampleFile);
        screen.ImportTitle.Should().Be("Review Sample note taker");
        server.Sent("POST", $"{root}/preview").Should().ContainSingle().Which.Should().Contain("sample.note-taker");
        server.Requests.Should().NotContain(request => request.Method == "POST" && request.Path == root, "nothing is added before the person says yes");
        handoff.Take().Should().BeNull("a handed file is read once");
    }

    [Fact]
    public async Task A_member_is_not_handed_a_file()
    {
        handoff.Give(SampleFile);

        var screen = await Opened(role: "member");

        screen.Importing.Should().BeFalse();
        server.Requests.Should().NotContain(request => request.Path == $"{root}/preview");
    }

    #endregion

    #region Tool Server Words

    [Fact]
    public void The_review_says_how_a_tool_server_is_called_in_words()
    {
        ExtensionWords.Auth(ServerPreview.Auth, ServerPreview.HeaderName).Should().Be(
            "A header named X-Api-Key. You type its value when you connect, and it is kept secret.");
        ExtensionWords.Auth("oauth", null).Should().Be("A sign-in at the server. Each person signs in as themselves on the Tools screen.");
        ExtensionWords.Auth("none", null).Should().Be("Nothing. The server asks for no credential.");
    }

    #endregion

    #region Tool Server Row

    [Fact]
    public async Task A_tool_server_is_connected_from_its_row_with_the_header_value_and_a_yes_to_a_local_address()
    {
        var local = Installed with { Connection = ServerState with { IsLocal = true } };
        server.Answer("POST", $"{root}/{extensionId}/connection", Installed with { Connection = Connected });
        var row = await Row(local);
        ExtensionSummary? changed = null;
        row.Changed += now => changed = now;

        row.Heading.Should().Be("Tool server: Helpdesk");
        row.Shows.Should().Be(ServerStep.Connect);
        row.HeaderValueLabel.Should().Be("Value of X-Api-Key");
        row.AcceptLocal.Should().BeFalse("the yes to a local address is the person's to give");
        row.LocalLabel.Should().Be("Yes, connect to tools.example.test. It is on this server's own network, or is not https.");
        row.ConnectLabel.Should().Be("Connect Helpdesk");
        row.HeaderValue = "sample-value";
        row.AcceptLocal = true;
        await row.ConnectCommand.ExecuteAsync(null);

        var sent = JsonSerializer.Deserialize<ConnectExtensionRequest>(server.Sent("POST", $"{root}/{extensionId}/connection").Single(), JsonSerializerOptions.Web)!;
        sent.Should().Be(new ConnectExtensionRequest(AcceptLocalAddress: true, HeaderValue: "sample-value"));
        row.HeaderValue.Should().BeEmpty("the value is gone from the screen once it is sent");
        changed.Should().NotBeNull();

        row.Show(changed!);
        row.Shows.Should().Be(ServerStep.Connected);
        row.StatusText.Should().Be("Connected");
        row.StatusIsReady.Should().BeTrue();
        row.LinkText.Should().Be("Open on the Tools screen");
        row.ToolsLegend.Should().Be("Tools suggested for Sample scribe");
        row.ShowsTools.Should().BeTrue();
        row.Connection.Tools.Select(tool => (row.IsOn(tool), ExtensionServerStepsViewModel.CanTick(tool))).Should().Equal(
            [(false, true), (false, false)],
            "no tool is ticked for the person, and one the server does not list cannot be");
        row.Connection.Tools.Select(ExtensionWords.ToolState).Should().Equal(string.Empty, "The server does not list this tool.");
    }

    [Fact]
    public async Task The_connect_button_says_it_is_working_while_the_server_is_asked()
    {
        var held = server.Hold("POST", $"{root}/{extensionId}/connection");
        var row = await Row(Installed with { Connection = ServerState });

        var connecting = row.ConnectCommand.ExecuteAsync(null);

        row.Busy.Should().BeTrue();
        row.ConnectLabel.Should().Be("Connecting");
        held.SetResult();
        await connecting;
    }

    [Fact]
    public async Task A_connection_the_server_refuses_says_why_and_keeps_the_form()
    {
        server.Refuse("POST", $"{root}/{extensionId}/connection", HttpStatusCode.BadRequest, "This address is on this server's own network, or is not https. Tick the box to say you mean to connect to it.");
        var row = await Row(Installed with { Connection = ServerState with { IsLocal = true } });
        row.HeaderValue = "sample-value";

        await row.ConnectCommand.ExecuteAsync(null);

        row.Problem.Should().Contain("Tick the box to say you mean to connect to it.");
        row.Shows.Should().Be(ServerStep.Connect);
        row.HeaderValue.Should().Be("sample-value");
    }

    [Fact]
    public async Task A_server_that_uses_no_header_sends_no_value()
    {
        server.Answer("POST", $"{root}/{extensionId}/connection", Installed with { Connection = Connected });
        var row = await Row(Installed with { Connection = ServerState with { Auth = "none", HeaderName = null } });
        row.HeaderValue = "left-over";

        await row.ConnectCommand.ExecuteAsync(null);

        var sent = JsonSerializer.Deserialize<ConnectExtensionRequest>(server.Sent("POST", $"{root}/{extensionId}/connection").Single(), JsonSerializerOptions.Web)!;
        sent.HeaderValue.Should().BeNull();
    }

    [Fact]
    public async Task The_bot_gets_only_the_tools_the_person_ticks_and_each_asks_first()
    {
        var granted = Connected with { Tools = [new SuggestedToolState("lookup", "Finds a ticket.", toolId, "ask"), new SuggestedToolState("reply", string.Empty, null, "off")] };
        server.Answer("PUT", $"/api/spaces/{spaceId}/bots/{botId}/tools/{toolId}", new BotToolEntry(toolId, "lookup", "Look up", "Sample description", "connection", null, "Helpdesk", "write", "ask", "allow", null, "available"));
        var row = await Row(Installed with { Connection = Connected });
        ExtensionSummary? changed = null;
        row.Changed += now => changed = now;

        row.GiveLabel.Should().Be("Give Sample scribe the ticked tools, asking first");
        row.ShowsGive.Should().BeTrue();
        row.CanGive.Should().BeFalse("there is nothing to give until a tool is ticked");
        row.Tick("lookup", on: true);
        row.CanGive.Should().BeTrue();
        server.Answer("GET", root, new[] { Installed with { Connection = granted } });
        await row.GrantCommand.ExecuteAsync(null);

        server.Sent("PUT", $"/api/spaces/{spaceId}/bots/{botId}/tools/{toolId}").Should().ContainSingle().Which.Should().Contain("ask");
        changed.Should().NotBeNull();
        row.Show(changed!);
        ExtensionWords.ToolState(row.Connection.Tools[0]).Should().Be("The bot has it, and asks first.");
        row.IsOn(row.Connection.Tools[0]).Should().BeTrue();
        row.ShowsGive.Should().BeFalse("nothing is left that could be given");
        row.IsTicked("lookup").Should().BeFalse();
        ExtensionWords.Status(changed!).Should().Be("Paused", "giving tools does not turn the bot on");
    }

    [Fact]
    public async Task Unticking_a_tool_leaves_nothing_to_give()
    {
        var row = await Row(Installed with { Connection = Connected });

        row.Tick("lookup", on: true);
        row.Tick("lookup", on: false);

        row.CanGive.Should().BeFalse();
    }

    [Fact]
    public async Task A_member_sees_how_far_a_tool_server_has_come_and_is_given_nothing_to_press()
    {
        var row = await Row(Installed with { Connection = ServerState }, role: "member");

        row.Shows.Should().Be(ServerStep.NotConnectedYet);
        row.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task Where_tool_connections_are_off_the_row_says_so_and_offers_no_connect()
    {
        var row = await Row(Installed with { Connection = ServerState with { Available = false } });

        row.Shows.Should().Be(ServerStep.TurnedOff);
    }

    [Fact]
    public async Task A_server_that_is_connected_and_not_answering_says_what_went_wrong()
    {
        var row = await Row(Installed with { Connection = Connected with { Status = "failed", Problem = "Sample failure.", Auth = "oauth", HeaderName = null } });

        row.Shows.Should().Be(ServerStep.Connected);
        row.StatusText.Should().Be("Not answering");
        row.StatusIsReady.Should().BeFalse();
        row.Connection.Problem.Should().Be("Sample failure.");
        row.LinkText.Should().Be("Sign in on the Tools screen");
        Addresses.Tools.Should().Be("/tools");
    }

    [Fact]
    public async Task A_server_that_is_connected_and_not_yet_listed_says_so()
    {
        var row = await Row(Installed with { Connection = Connected with { Status = "pending" } });

        row.StatusText.Should().Be("Connected, not listed yet");
    }

    [Fact]
    public async Task An_extension_that_only_names_a_tool_server_says_so_and_has_no_bot_to_turn_on()
    {
        var only = Installed with { AddsBot = false, BotId = null, BotName = null, BotStatus = null, Connection = Connected };
        var screen = await Opened(only);
        var row = await Row(only);

        ExtensionWords.Adds(only).Should().Be("Names the tool server Helpdesk.");
        row.ShowsTools.Should().BeFalse("there is no bot to give tools to");
        screen.ShowsTurnOn(only).Should().BeFalse("there is no bot to turn on");
    }

    #endregion

    #region Private Methods

    private ExtensionsViewModel Make() => ActivatorUtilities.CreateInstance<ExtensionsViewModel>(services);

    private async Task<ExtensionsViewModel> Opened(params ExtensionSummary[] extensions) => await Opened("owner", extensions);

    private async Task<ExtensionsViewModel> Opened(string role, params ExtensionSummary[] extensions)
    {
        server.Answer("GET", "/api/session", Samples.Session(role));
        server.Answer("GET", root, extensions);
        var screen = Make();
        await screen.Open();
        return screen;
    }

    private async Task<ExtensionServerStepsViewModel> Row(ExtensionSummary extension, string role = "owner")
    {
        server.Answer("GET", "/api/session", Samples.Session(role));
        await session.Load();
        var row = ActivatorUtilities.CreateInstance<ExtensionServerStepsViewModel>(services);
        row.Show(extension);
        return row;
    }

    /// <summary>Opens the import, pastes the sample file, and asks the server to read it.</summary>
    private static async Task ReadSample(ExtensionsViewModel screen)
    {
        screen.StartImporting();
        screen.Form.Text = SampleFile;
        await screen.ReadFormCommand.ExecuteAsync(null);
    }

    private static BotDetail Bot(string status) =>
        new(
            botId, "Sample scribe", "sample-scribe", new BotAvatarBody(null, null), "Sample purpose", "Keep notes short.",
            Guid.Parse("0196f1a0-0000-7000-8000-000000000023"), "sample-default", new ModelSettingsBody(null, null, null, "off", false),
            new MemorySettingsBody(true, "none", null, null, 300, 4000), "none", new SandboxSettingsBody("off", [], null), [],
            status, null, 0, new BuildLimitsBody(null, null), new RunLimitsBody(null, null, null), null, null);

    #endregion
}
