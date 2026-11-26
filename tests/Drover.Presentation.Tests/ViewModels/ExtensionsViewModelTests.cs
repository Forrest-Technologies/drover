namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The extensions screen's view model against a stand-in server: what each state says, what importing and removing
/// send, how a tool server is connected with its settings, and what a person who is not an admin may do. Every name
/// and value here is a sample.
/// </summary>
public sealed class ExtensionsViewModelTests : IDisposable
{
    #region Private Fields

    private const string SampleFile = """{"version":2,"id":"sample.helpdesk","name":"Sample helpdesk","contents":[{"kind":"mcp_connection","name":"Helpdesk","url":"https://tools.example.test/mcp"}]}""";

    private static readonly Guid spaceId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static readonly Guid extensionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000021");

    private static readonly Guid toolId = Guid.Parse("0196f1a0-0000-7000-8000-000000000024");

    private static readonly string root = $"/api/spaces/{spaceId}/extensions";

    private static readonly BotRuleBody sampleRule =
        new("Sample: ask before writing", true, "before_tool", "ask", [new RuleConditionBody("tool_kind") { ToolKinds = ["write"] }]);

    private readonly FakeServer server = new();

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
            .BuildServiceProvider();
    }

    #endregion

    #region Properties

    private static ExtensionParts Offered { get; } = new()
    {
        Themes = [new ExtensionTheme("sample-dusk", "Sample dusk", "A sample look.")],
        Rules = [sampleRule],
    };

    private static ExtensionConnectionSummary ServerState { get; } =
        new("Helpdesk", "https://tools.example.test/mcp", "tools.example.test", "header", "X-Api-Key", IsLocal: false, Available: true)
        {
            AsksForHeader = true,
            Tools = [new SuggestedToolState("lookup", "Finds a ticket.", null), new SuggestedToolState("reply", string.Empty, null)],
        };

    private static ExtensionConnectionSummary Connected { get; } = ServerState with
    {
        ConnectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000025"),
        Status = "ready",
        Tools = [new SuggestedToolState("lookup", "Finds a ticket.", toolId), new SuggestedToolState("reply", string.Empty, null)],
    };

    private static ExtensionSummary Installed { get; } =
        new(extensionId, "sample.helpdesk", "Sample helpdesk", "Sample description", DateTimeOffset.UnixEpoch) { Connection = ServerState, Parts = Offered };

    private static ExtensionPreview Preview { get; } = new("sample.helpdesk", "Sample helpdesk", "Sample description")
    {
        Connection = new("Helpdesk", "https://tools.example.test/mcp", "tools.example.test", "header", "X-Api-Key", IsLocal: false, Available: true)
        {
            AsksForHeader = true,
            SuggestedTools = [new SuggestedTool("lookup", "Finds a ticket."), new SuggestedTool("reply")],
        },
        Parts = Offered,
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
        ExtensionsViewModel.EmptyCause.Should().Contain("a tool server to connect, themes to choose, rules to give a bot")
            .And.Contain("it can give no bot a tool");
        screen.ShowsActions.Should().BeTrue();
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
    public async Task A_list_the_server_would_not_give_says_why_and_loads_again()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        server.Refuse("GET", root, HttpStatusCode.InternalServerError, "Sample failure.");
        var screen = Make();
        await screen.Open();

        screen.Shows.Should().Be(ExtensionsContent.LoadFailed);
        ExtensionsViewModel.LoadFailedTitle.Should().Be("Drover could not load the extensions");
        screen.LoadProblem.Should().NotBeNullOrWhiteSpace();
        server.Answer("GET", root, (ExtensionSummary[])[Installed]);

        await screen.LoadCommand.ExecuteAsync(null);

        screen.Shows.Should().Be(ExtensionsContent.List);
    }

    [Fact]
    public async Task An_installed_extension_says_what_it_holds_in_one_line()
    {
        var screen = await Opened(Installed);

        screen.Shows.Should().Be(ExtensionsContent.List);
        ExtensionWords.Holds(Installed).Should().Be("Tool server Helpdesk · 1 theme · 1 rule");
        ExtensionWords.Holds(Installed with { Connection = null, Parts = new() { Themes = [.. Offered.Themes, .. Offered.Themes] } }).Should().Be("2 themes");
        ExtensionWords.Holds(Installed with { Connection = null, Parts = new() }).Should().Be(
            "Holds nothing now. You can remove it.",
            "a file kept from when an extension could hold a bot may hold nothing else");
    }

    [Fact]
    public async Task A_member_sees_the_extensions_and_is_offered_nothing_to_change()
    {
        var empty = await Opened(role: "member");
        var listed = await Opened("member", Installed);
        var row = await Row(Installed, "member");

        empty.Shows.Should().Be(ExtensionsContent.EmptyForMember);
        ExtensionsViewModel.EmptyForMember.Should().Be("This space has no extensions yet. An owner or admin of the space can import one.");
        listed.Shows.Should().Be(ExtensionsContent.List);
        listed.ShowsActions.Should().BeFalse();
        listed.CanEdit.Should().BeFalse("the row offers no Remove");
        row.Shows.Should().Be(ServerStep.NotConnectedYet);
    }

    #endregion

    #region Import

    [Fact]
    public async Task Reading_a_file_shows_what_it_holds_and_importing_sends_the_same_text()
    {
        server.Answer("POST", $"{root}/preview", Preview);
        server.Answer("POST", root, Installed, HttpStatusCode.Created);
        var screen = await Opened();

        screen.StartImporting();
        var titleBefore = screen.ImportTitle;
        var canReadBefore = screen.Form.CanRead;
        await ReadSample(screen);
        var titleAfter = screen.ImportTitle;
        var canImport = screen.CanImport;
        await screen.ImportCommand.ExecuteAsync(null);

        titleBefore.Should().Be("Import an extension");
        screen.Form.FileLabel.Should().Be("Extension file");
        canReadBefore.Should().BeFalse("there is nothing to read until there is some text");
        titleAfter.Should().Be("Review Sample helpdesk");
        canImport.Should().BeTrue();
        server.Sent("POST", $"{root}/preview").Should().ContainSingle().Which.Should().Contain("sample.helpdesk");
        server.Sent("POST", root).Should().ContainSingle().Which.Should().Be(server.Sent("POST", $"{root}/preview").Single(), "the text that was read is the text that is imported");
        screen.Importing.Should().BeFalse();
        screen.Extensions.Should().ContainSingle().Which.Id.Should().Be(extensionId);
        screen.Notice.Should().Be("Sample helpdesk is imported. Connect its tool server below.");
        screen.ImportLabel.Should().Be("Import");
    }

    [Fact]
    public async Task A_file_the_server_cannot_read_says_why_and_stays_on_the_first_step()
    {
        server.Refuse("POST", $"{root}/preview", HttpStatusCode.BadRequest, "This is not an extension file Drover can read.");
        var screen = await Opened();

        await ReadSample(screen);

        screen.Reviewed.Should().BeNull();
        screen.ImportProblem.Should().Contain("This is not an extension file Drover can read.");
        screen.CanImport.Should().BeFalse();
        screen.Extensions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_extension_that_is_already_in_the_space_is_reviewed_and_cannot_be_imported_again()
    {
        server.Answer("POST", $"{root}/preview", Preview with { AlreadyInstalled = true });
        var screen = await Opened(Installed);

        await ReadSample(screen);

        screen.CanImport.Should().BeFalse();
    }

    [Fact]
    public async Task The_buttons_say_they_are_working_while_the_server_is_asked()
    {
        var held = server.Hold("POST", $"{root}/preview");
        var screen = await Opened();
        screen.StartImporting();
        screen.Form.Text = SampleFile;

        var reading = screen.ReadFormCommand.ExecuteAsync(null);

        screen.ReadLabel.Should().Be("Reading the file");
        screen.ImportBusy.Should().BeTrue();
        held.SetResult();
        await reading;
        screen.ReadLabel.Should().Be("Read the file");
    }

    [Fact]
    public async Task A_file_too_large_to_be_an_extension_is_not_opened()
    {
        var screen = await Opened();
        screen.StartImporting();
        var opened = false;

        await screen.Form.Choose(300 * 1024, () =>
        {
            opened = true;
            return Task.FromResult(SampleFile);
        });

        opened.Should().BeFalse();
        screen.Form.FileProblem.Should().Be("This file is larger than an extension can be (256 KB).");
    }

    #endregion

    #region Remove

    [Fact]
    public async Task Removing_asks_first_says_what_goes_and_then_takes_the_extension_out()
    {
        var connected = Installed with { Connection = Connected };
        server.Answer("DELETE", $"{root}/{extensionId}", HttpStatusCode.NoContent);
        var screen = await Opened(connected);

        screen.AskToRemove(connected);
        var asked = screen.Removing;
        var label = screen.RemoveLabel;
        var sentBefore = server.Sent("DELETE", $"{root}/{extensionId}").Count();
        await screen.RemoveCommand.ExecuteAsync(null);

        asked.Should().Be(connected);
        label.Should().Be("Remove Sample helpdesk");
        sentBefore.Should().Be(0, "nothing is removed until the person says so");
        ExtensionWords.RemoveQuestion(connected).Should().Be("Remove Sample helpdesk?");
        ExtensionWords.RemoveConsequence(connected).Should()
            .Contain("The connection to Helpdesk is removed, with its credentials and every bot's grants on its tools.")
            .And.Contain("A person using one goes back to the default look.")
            .And.Contain("A rule already added to a bot stays.");
        ExtensionWords.RemoveConsequence(Installed with { Connection = null, Parts = new() }).Should().Be("It holds nothing, so nothing else changes.");
        ExtensionWords.RemoveConsequence(Installed).Should().NotContain("The connection", "a tool server that was never connected has no connection to remove");
        screen.Extensions.Should().BeEmpty();
        screen.Removing.Should().BeNull();
        screen.Notice.Should().Be("Sample helpdesk is removed.");
        screen.Shows.Should().Be(ExtensionsContent.Empty);
    }

    [Fact]
    public async Task Keeping_an_extension_sends_nothing_and_a_refused_removal_says_why_and_keeps_it()
    {
        server.Refuse("DELETE", $"{root}/{extensionId}", HttpStatusCode.BadRequest, "Sample refusal.");
        var screen = await Opened(Installed);

        screen.AskToRemove(Installed);
        screen.KeepIt();
        var sentAfterKeeping = server.Sent("DELETE", $"{root}/{extensionId}").Count();
        screen.AskToRemove(Installed);
        await screen.RemoveCommand.ExecuteAsync(null);

        sentAfterKeeping.Should().Be(0);
        screen.RemoveProblem.Should().Contain("Sample refusal.");
        screen.Removing.Should().Be(Installed, "the question stays open with the reason in it");
        screen.Extensions.Should().ContainSingle();
        screen.RemoveBusy.Should().BeFalse();
    }

    #endregion

    #region Tool Server

    [Fact]
    public async Task A_tool_server_is_connected_with_the_header_value_the_person_types_and_the_row_then_says_it_is_connected()
    {
        server.Answer("POST", $"{root}/{extensionId}/connection", Installed with { Connection = Connected });
        var row = await Row(Installed);
        ExtensionSummary? changed = null;
        row.Changed += now => changed = now;

        var stepBefore = row.Shows;
        row.HeaderValue = "sample-value";
        await row.ConnectCommand.ExecuteAsync(null);
        row.Show(changed!);

        stepBefore.Should().Be(ServerStep.Connect);
        row.Heading.Should().Be("Tool server: Helpdesk");
        row.HeaderValueLabel.Should().Be("Value of X-Api-Key");
        row.AsksAboutLocal.Should().BeFalse();
        server.Sent("POST", $"{root}/{extensionId}/connection").Should().ContainSingle()
            .Which.Should().Contain("\"headerValue\":\"sample-value\"").And.Contain("\"acceptLocalAddress\":false");
        row.HeaderValue.Should().BeEmpty("a secret is not kept in the field once it is sent");
        row.Shows.Should().Be(ServerStep.Connected);
        row.StatusText.Should().Be("Connected");
        row.StatusIsReady.Should().BeTrue();
        row.ShowsTools.Should().BeTrue();
        ExtensionWords.ToolState(row.Connection.Tools[0]).Should().BeEmpty();
        ExtensionWords.ToolState(row.Connection.Tools[1]).Should().Be("The server does not list this tool.");
        ExtensionServerStepsViewModel.GrantHint.Should().Be("You give a tool to a bot in the bot's settings, under Tools.");
    }

    [Fact]
    public async Task Settings_start_with_what_the_file_gives_them_and_are_sent_with_the_connection()
    {
        var region = new ExtensionSetting("region", "Region", ExtensionSetting.Choice) { Choices = ["eu", "us"], Default = "eu" };
        var beta = new ExtensionSetting("beta", "Use the beta server", ExtensionSetting.Toggle);
        var token = new ExtensionSetting("token", "API key", ExtensionSetting.Secret) { Help = "Under Account, then API." };
        var configured = Installed with
        {
            Connection = ServerState with { Host = "{region}.tools.example.test", DependsOnSettings = true, AsksForHeader = false },
            Parts = new() { Settings = [region, beta, token] },
        };
        server.Answer("POST", $"{root}/{extensionId}/connection", configured with
        {
            Connection = Connected with { AsksForHeader = false },
            FilledSettings = new Dictionary<string, string> { ["region"] = "us", ["beta"] = "false" },
        });
        var row = await Row(configured);
        ExtensionSummary? changed = null;
        row.Changed += now => changed = now;

        var starts = (row.Value(region), row.Value(beta), row.Value(token));
        var (asksAboutLocal, localLabel) = (row.AsksAboutLocal, row.LocalLabel);
        row.SetValue(region, "us");
        row.SetValue(token, "sample-value");
        await row.ConnectCommand.ExecuteAsync(null);
        row.Show(changed!);

        starts.Should().Be(("eu", "false", string.Empty), "a choice starts with the file's value, a switch off, and a secret empty");
        row.AsksForHeaderValue.Should().BeFalse("the header is made from the secret setting");
        asksAboutLocal.Should().BeTrue("only the server knows, once the settings are filled in, whether the address is local");
        localLabel.Should().Be("Yes, connect even if the address is on this server's own network, or is not https.");
        ExtensionWords.SettingHint(token).Should().Be("Under Account, then API. Stored encrypted. It is not shown again.");
        ExtensionWords.SettingHint(region).Should().BeEmpty();
        server.Sent("POST", $"{root}/{extensionId}/connection").Should().ContainSingle()
            .Which.Should().Contain("\"region\":\"us\"").And.Contain("\"beta\":\"false\"").And.Contain("\"token\":\"sample-value\"").And.Contain("\"headerValue\":null");
        row.Value(region).Should().Be("us", "a field then shows what the extension was connected with");
        row.Value(token).Should().BeEmpty("a secret is never given back");
    }

    [Fact]
    public async Task A_local_address_asks_for_a_yes_and_a_refused_connection_says_why()
    {
        var local = Installed with { Connection = ServerState with { Url = "http://nas/mcp", Host = "nas", IsLocal = true, Auth = "none", HeaderName = null, AsksForHeader = false } };
        server.Refuse("POST", $"{root}/{extensionId}/connection", HttpStatusCode.BadRequest, "Sample refusal.");
        var row = await Row(local);

        row.AcceptLocal = true;
        await row.ConnectCommand.ExecuteAsync(null);

        row.AsksAboutLocal.Should().BeTrue();
        row.LocalLabel.Should().Be("Yes, connect to nas. It is on this server's own network, or is not https.");
        row.ConnectLead.Should().Be("No credential. The server asks for none.");
        server.Sent("POST", $"{root}/{extensionId}/connection").Should().ContainSingle().Which.Should().Contain("\"acceptLocalAddress\":true");
        row.Problem.Should().Contain("Sample refusal.");
        row.Shows.Should().Be(ServerStep.Connect);
        row.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task A_server_with_tool_connections_off_says_so_and_offers_no_connect()
    {
        var row = await Row(Installed with { Connection = ServerState with { Available = false } });

        row.Shows.Should().Be(ServerStep.TurnedOff);
        ExtensionWords.Imported(Installed with { Connection = ServerState with { Available = false } }).Should().Be("Sample helpdesk is imported.");
    }

    [Theory]
    [InlineData("header", true, "A header named X-Api-Key. You type its value when you connect, and it is kept secret.")]
    [InlineData("header", false, "A header named X-Api-Key, made from a secret setting you fill in when you connect. It is kept secret.")]
    [InlineData("oauth", false, "A sign-in at the server. Each person signs in as themselves on the Tools screen.")]
    [InlineData("none", false, "No credential. The server asks for none.")]
    public void How_drover_signs_in_is_said_in_words(string auth, bool asks, string words) =>
        ExtensionWords.Auth(auth, "X-Api-Key", asks).Should().Be(words);

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

    #endregion
}
