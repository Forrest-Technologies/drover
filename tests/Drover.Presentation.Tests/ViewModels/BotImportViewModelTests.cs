namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// Bringing a bot in from a file on the bots screen, against a stand-in server: the file is read for review, the
/// same text is imported, and the bot's settings open so it can be set up and turned on. Every value is a sample.
/// </summary>
public sealed class BotImportViewModelTests : ViewModelTest
{
    #region Private Fields

    private const string SampleFile = """{"version":1,"name":"Sample scribe","instructions":"Keep notes short."}""";

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000031");

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000032");

    private static readonly BotImportPreview preview = new("Sample scribe", "Sample purpose", "Keep notes short.", 0)
    {
        Model = "sample-model",
        ModelConnection = "Sample connection",
    };

    private static readonly ModelConnectionSummary connection =
        new(connectionId, "Sample connection", "venice", null, true, "connected", "sample-model", [], null, null);

    #endregion

    #region Tests

    [Fact]
    public async Task Reading_a_bot_file_shows_the_bot_and_importing_sends_the_same_text_and_opens_its_settings()
    {
        Server.Answer("POST", $"{space}/bots/import/preview", preview);
        Server.Answer("POST", $"{space}/bots/import", new BotSummary(botId, "Sample scribe", "sample-scribe", "Keep notes short.", connectionId, "sample-model", "paused"), HttpStatusCode.Created);
        var screen = await Opened();

        screen.StartImporting();
        var titleBefore = screen.ImportTitle;
        screen.Form.Text = SampleFile;
        await screen.ReadFormCommand.ExecuteAsync(null);
        var (titleAfter, canImport) = (screen.ImportTitle, screen.CanImport);
        await screen.ImportCommand.ExecuteAsync(null);

        screen.ShowsImport.Should().BeTrue();
        BotListViewModel.ImportBotLabel.Should().Be("Import a bot");
        titleBefore.Should().Be("Import a bot");
        screen.Form.FileLabel.Should().Be("Bot file");
        titleAfter.Should().Be("Review Sample scribe");
        canImport.Should().BeTrue();
        screen.ImportLabel.Should().Be("Import, paused");
        Server.Sent("POST", $"{space}/bots/import").Should().ContainSingle()
            .Which.Should().Be(Server.Sent("POST", $"{space}/bots/import/preview").Single(), "the text that was read is the text that is imported");
        screen.Importing.Should().BeFalse();
        Navigator.Current.Should().Be(Addresses.Bot(botId), "a bot from a file is set up and turned on in its own settings");
        Toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be("Sample scribe is imported and paused. Set it up, then turn it on.");
    }

    [Fact]
    public async Task A_bot_with_no_model_to_think_with_is_reviewed_and_cannot_be_imported()
    {
        Server.Answer("POST", $"{space}/bots/import/preview", preview with { Model = null, ModelConnection = null });
        var screen = await Opened();

        screen.StartImporting();
        screen.Form.Text = SampleFile;
        await screen.ReadFormCommand.ExecuteAsync(null);

        screen.Reviewed.Should().NotBeNull();
        screen.CanImport.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_the_server_cannot_read_says_why_and_a_refused_import_adds_nothing()
    {
        Server.Refuse("POST", $"{space}/bots/import/preview", HttpStatusCode.BadRequest, "This is an extension file, not a bot file. Import it on the Extensions screen.");
        var screen = await Opened();

        screen.StartImporting();
        screen.Form.Text = SampleFile;
        await screen.ReadFormCommand.ExecuteAsync(null);

        screen.Reviewed.Should().BeNull();
        screen.ImportProblem.Should().Contain("This is an extension file, not a bot file.");
        screen.ImportBusy.Should().BeFalse();
        Navigator.Current.Should().NotBe(Addresses.Bot(botId));
        BotFileWords.Hidden(1).Should().StartWith("These instructions hold 1 character that does not show on a screen.");
        BotFileWords.Hidden(3).Should().StartWith("These instructions hold 3 characters that do not show on a screen.");
    }

    [Fact]
    public async Task Importing_is_offered_to_an_admin_of_a_space_with_a_model_and_to_nobody_else()
    {
        var withModel = await Opened();
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        var withoutModel = Make<BotListViewModel>();
        await withoutModel.Open();

        withModel.ShowsImport.Should().BeTrue();
        withoutModel.ShowsImport.Should().BeFalse("a bot from a file needs a model to think with, as any new bot does");
    }

    #endregion

    #region Helpers

    private async Task<BotListViewModel> Opened()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[connection]);
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        var screen = Make<BotListViewModel>();
        await screen.Open();
        return screen;
    }

    #endregion
}
