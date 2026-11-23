namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Files in the conversation and beside it, as view models: what is shown, what each control is named for assistive
/// technology, and what is handed back. The cases are the web client's for the card and the files panel.
/// </summary>
public sealed class FileComponentTests : IDisposable
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly BotSummary marshal = new(botId, "Marshal", "marshal", "Sample instructions", Guid.Empty, "sample-default", "active");

    private readonly ChatBench bench = new();

    #endregion

    #region Public Methods

    public void Dispose() => bench.Dispose();

    #endregion

    #region Card

    [Fact]
    public void A_picture_has_its_dimensions_and_every_file_can_be_downloaded_by_name()
    {
        var picture = new SharedFile(Guid.NewGuid(), "chart.png", "image/png", 9000, "/files/1", true, start, Width: 640, Height: 480);
        var card = Card(picture);

        card.Kind.Should().Be(FileKind.Image);
        (card.File.Width, card.File.Height).Should().Be((640, 480));
        card.PictureName.Should().Be("chart.png");
        card.Delivers.Should().BeFalse();
        card.DownloadName.Should().Be("Download chart.png");
        card.Facts.Should().Be("PNG image, 8.8 KB");
    }

    [Fact]
    public async Task The_card_on_a_device_saves_or_shares_the_selected_file()
    {
        var delivery = new SampleDelivery();
        var report = new SharedFile(Guid.NewGuid(), "report.txt", "text/plain", 100, "/files/1", true, start);
        var card = new FileCardViewModel(NullLogger<FileCardViewModel>.Instance, delivery) { File = report };

        card.Delivers.Should().BeTrue();
        (card.SaveName, card.ShareName).Should().Be(("Save report.txt", "Share report.txt"));
        await card.Export(save: true);
        card.ExportStatus.Should().Be("Saved report.txt.");
        card.ExportFailed.Should().BeFalse();
        await card.Export(save: false);

        delivery.Saved.Should().Be(report);
        delivery.Shared.Should().Be(report);
        card.Exporting.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_the_device_could_not_take_says_so()
    {
        var report = new SharedFile(Guid.NewGuid(), "report.txt", "text/plain", 100, "/files/1", true, start);
        var card = new FileCardViewModel(NullLogger<FileCardViewModel>.Instance, new SampleDelivery { Fails = true }) { File = report };

        await card.Export(save: true);
        (card.ExportFailed, card.ExportStatus).Should().Be((true, "Could not save report.txt."));
        await card.Export(save: false);

        (card.ExportFailed, card.ExportStatus).Should().Be((true, "Could not share report.txt."));
    }

    [Fact]
    public async Task A_failed_export_says_why_where_the_platform_or_the_server_says_so_and_nothing_else()
    {
        var report = new SharedFile(Guid.NewGuid(), "report.txt", "text/plain", 100, "/files/1", true, start);
        var delivery = new SampleDelivery { Failure = new FileDeliveryException("This device has no file picker.") };
        var card = new FileCardViewModel(NullLogger<FileCardViewModel>.Instance, delivery) { File = report };

        await card.Export(save: true);
        card.ExportStatus.Should().Be("Could not save report.txt. This device has no file picker.");

        delivery.Failure = new HttpRequestException("Sample: nothing answered.");
        await card.Export(save: false);
        card.ExportStatus.Should().Be($"Could not share report.txt. {Failures.Unreachable}");

        delivery.Failure = new IOException("Sample: /private/path/in/a/system/error");
        await card.Export(save: true);
        card.ExportStatus.Should().Be("Could not save report.txt.", "a system error can name a path, so it is not shown");
    }

    [Fact]
    public async Task A_press_while_a_file_is_being_exported_does_nothing_and_says_it_is_working()
    {
        var report = new SharedFile(Guid.NewGuid(), "report.txt", "text/plain", 100, "/files/1", true, start);
        var release = new TaskCompletionSource();
        var delivery = new SampleDelivery { Hold = release.Task };
        var card = new FileCardViewModel(NullLogger<FileCardViewModel>.Instance, delivery) { File = report };

        var first = card.Export(save: true);
        card.Exporting.Should().BeTrue();
        card.ExportingWords.Should().Be("Working");
        await card.Export(save: true);
        release.SetResult();
        await first;

        delivery.SaveCalls.Should().Be(1, "the second press started nothing");
        card.ExportingWords.Should().BeNull();
    }

    [Fact]
    public void A_file_a_bot_shared_shows_its_caption_and_what_it_is()
    {
        var report = new SharedFile(Guid.NewGuid(), "report.pdf", "application/pdf", 3_400_000, "/api/files/report", true, start, Caption: "The weekly numbers");
        var card = Card(report);

        card.Kind.Should().Be(FileKind.Other);
        card.Facts.Should().Be("PDF document, 3.2 MB");
        card.Caption.Should().Be("The weekly numbers");
        card.DownloadName.Should().Be("Download report.pdf");
    }

    [Fact]
    public void A_text_file_shows_its_first_lines_named_as_such()
    {
        var text = new SharedFile(Guid.NewGuid(), "notes.txt", "text/plain", 12, "/api/files/notes", false, start, Preview: "first line");
        var card = Card(text);

        card.Kind.Should().Be(FileKind.Text);
        card.PreviewName.Should().Be("The first lines of notes.txt");
        card.PreviewText.Should().Be("first line");
        card.DownloadName.Should().Be("Download notes.txt");
    }

    [Fact]
    public async Task Show_all_brings_a_text_files_whole_content_into_its_box_and_says_when_it_is_cut()
    {
        var longFile = new SharedFile(Guid.NewGuid(), "log.txt", "text/plain", 5_000, "/api/files/log", true, start, Preview: "line one\nline two");
        SharedFile? asked = null;
        var answer = new FileText("line one\nline two\nline three", Cut: false);
        var card = Card(longFile, file =>
        {
            asked = file;
            return Task.FromResult(answer);
        });

        card.CanShowAll.Should().BeTrue();
        await card.ShowAll();

        asked.Should().Be(longFile);
        card.PreviewName.Should().Be("All of log.txt");
        card.PreviewText.Should().Be("line one\nline two\nline three");
        card.CanShowAll.Should().BeFalse("everything is shown");
        card.TakeFocusForText().Should().BeTrue("the button is gone, so the focus moves to the text it brought");
        card.TakeFocusForText().Should().BeFalse("once");

        answer = answer with { Cut = true };
        var cut = Card(longFile, _ => Task.FromResult(answer));
        (cut.ShowAllLabel, cut.ShowAllName).Should().Be(("Show all", "Show all of log.txt"));
        await cut.ShowAll();
        cut.CutNote.Should().Be("Only the first 100,000 characters of log.txt are shown. Download it for the rest.");
    }

    [Fact]
    public async Task While_the_whole_text_is_read_the_button_says_so_and_a_second_press_does_nothing()
    {
        var file = new SharedFile(Guid.NewGuid(), "log.txt", "text/plain", 5_000, "/api/files/log", true, start, Preview: "line one");
        var reads = 0;
        var held = new TaskCompletionSource<FileText>();
        var card = Card(file, _ =>
        {
            reads++;
            return held.Task;
        });

        var first = card.ShowAll();
        (card.ShowAllLabel, card.ShowAllName).Should().Be(("Loading", "Loading all of log.txt"));
        await card.ShowAll();
        held.SetResult(new("line one\nline two", Cut: false));
        await first;

        reads.Should().Be(1);
        card.PreviewText.Should().Be("line one\nline two");
    }

    [Fact]
    public void A_text_file_that_shows_all_of_itself_or_a_card_with_no_reader_offers_no_show_all()
    {
        var whole = new SharedFile(Guid.NewGuid(), "short.txt", "text/plain", 19, "/api/files/short", true, start, Preview: "line one\nline two");
        var longer = whole with { Size = 5_000 };

        Card(whole, _ => Task.FromResult(new FileText(string.Empty, false))).CanShowAll
            .Should().BeFalse("a file of 17 characters, two line breaks, and a carriage return has nothing more");
        Card(longer).CanShowAll.Should().BeFalse("there is no way to read it");
    }

    [Fact]
    public async Task Show_all_that_fails_says_so_and_can_be_tried_again()
    {
        var file = new SharedFile(Guid.NewGuid(), "log.txt", "text/plain", 5_000, "/api/files/log", true, start, Preview: "line one");
        var card = Card(file, _ => throw new HttpRequestException("down"));

        await card.ShowAll();

        card.Problem.Should().Contain("Drover could not load the rest of log.txt.");
        card.CanShowAll.Should().BeTrue();
        card.ShowAllName.Should().Be("Show all of log.txt");
    }

    [Fact]
    public async Task A_bot_html_file_opens_one_isolated_preview_and_revokes_its_grant_when_closed()
    {
        var file = new SharedFile(Guid.NewGuid(), "report.html", "text/html", 100, "/api/files/report", true, start);
        var requested = 0;
        string? revoked = null;
        var card = Card(file);
        card.RequestPreview = _ =>
        {
            requested++;
            return Task.FromResult(new FilePreviewResponse("https://preview.example/preview/file?grant=hidden", Grant: "hidden"));
        };
        card.RevokePreview = (_, grant) =>
        {
            revoked = grant;
            return Task.CompletedTask;
        };

        await card.Preview();

        card.CanPreview.Should().BeTrue();
        card.PreviewAddress.Should().Be("https://preview.example/preview/file?grant=hidden");
        card.PreviewProblem.Should().BeNull();
        requested.Should().Be(1);

        await card.ClosePreview();

        card.PreviewAddress.Should().BeNull();
        revoked.Should().Be("hidden");
    }

    [Fact]
    public async Task An_unavailable_html_preview_keeps_a_plain_download_fallback()
    {
        var file = new SharedFile(Guid.NewGuid(), "report.html", "text/html", 100, "/api/files/report", true, start);
        var card = Card(file);
        card.RequestPreview = _ => Task.FromResult(new FilePreviewResponse(null,
            "HTML preview is unavailable. Download is still available."));

        await card.Preview();

        card.PreviewAddress.Should().BeNull();
        card.PreviewProblem.Should().Be("HTML preview is unavailable. Download is still available.");
        card.DownloadName.Should().Be("Download report.html");

        card.RefusePreview();
        card.PreviewProblem.Should().Be("Preview is not available on this device. Save the file to view it.");
    }

    [Fact]
    public async Task A_preview_request_in_progress_ignores_a_second_press()
    {
        var file = new SharedFile(Guid.NewGuid(), "report.html", "text/html", 100, "/api/files/report", true, start);
        var held = new TaskCompletionSource<FilePreviewResponse>();
        var requested = 0;
        var card = Card(file);
        card.RequestPreview = _ =>
        {
            requested++;
            return held.Task;
        };

        var first = card.Preview();
        card.PreviewButtonName.Should().Be("Loading a preview of report.html");
        await card.Preview();
        held.SetResult(new("https://preview.example/preview/file?grant=hidden", Grant: "hidden"));
        await first;

        requested.Should().Be(1);
        card.Previewing.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_late_grant_is_revoked_against_the_original_card_file(bool replace)
    {
        var file = new SharedFile(Guid.NewGuid(), "report.html", "text/html", 100, "/api/files/report", true, start);
        var card = Card(file);
        var held = new TaskCompletionSource<FilePreviewResponse>();
        SharedFile? revoked = null;
        card.RequestPreview = _ => held.Task;
        card.RevokePreview = (selected, grant) =>
        {
            revoked = selected;
            grant.Should().Be("hidden");
            return Task.CompletedTask;
        };

        var request = card.Preview();
        if (replace)
        {
            card.File = file with { Id = Guid.NewGuid(), Name = "replacement.html" };
        }
        else
        {
            await card.ClosePreview();
        }

        held.SetResult(new("https://preview.example/preview/file?grant=hidden", Grant: "hidden"));
        await request;

        revoked.Should().Be(file);
        card.PreviewAddress.Should().BeNull();
        card.Previewing.Should().BeFalse();
    }

    [Fact]
    public void A_person_html_file_is_downloaded_without_a_preview_offer()
    {
        var file = new SharedFile(Guid.NewGuid(), "notes.html", "TEXT/HTML", 100, "/api/files/notes", false, start);

        Card(file).CanPreview.Should().BeFalse();
        Card(file with { FromBot = true }).CanPreview.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://app.example.test/preview/file?grant=hidden")]
    [InlineData("https://preview.example.test/preview/file?grant=hidden")]
    public async Task An_html_preview_on_the_app_servers_own_site_is_refused_and_revoked(string sameSiteAddress)
    {
        var file = new SharedFile(Guid.NewGuid(), "report.html", "text/html", 100, "https://app.example.test/api/spaces/1/files/2", true, start);
        string? revoked = null;
        var card = Card(file);
        card.RequestPreview = _ => Task.FromResult(new FilePreviewResponse(sameSiteAddress, Grant: "hidden"));
        card.RevokePreview = (_, grant) =>
        {
            revoked = grant;
            return Task.CompletedTask;
        };

        await card.Preview();

        card.PreviewAddress.Should().BeNull();
        card.PreviewProblem.Should().Be("Drover could not preview report.html. Download is still available.");
        revoked.Should().Be("hidden");
    }

    #endregion

    #region Files Panel

    [Fact]
    public void The_files_filter_appears_only_when_files_came_from_both_sides()
    {
        var fromBot = new SharedFile(Guid.NewGuid(), "a.pdf", "application/pdf", 10, "/files/a", true, start);
        var panel = new FilesPanelViewModel { State = FilesPanelState.Ready, BotName = "Sample bot", Files = [fromBot] };

        panel.ShowsFilter.Should().BeFalse();
        panel.Origin(fromBot).Should().StartWith("From Sample bot");

        panel.Files = [fromBot, fromBot with { Id = Guid.NewGuid(), FromBot = false }];

        panel.ShowsFilter.Should().BeTrue();
        FilesPanelViewModel.FilterName.Should().Be("Whose files");
        panel.Sides.Select(side => side.Label).Should().Equal("All", "From Sample bot", "From you");
    }

    [Fact]
    public void The_files_panel_lists_each_file_with_who_it_came_from_when_and_a_link_to_that_moment()
    {
        var fromBot = new SharedFile(Guid.NewGuid(), "a.pdf", "application/pdf", 10, "/files/a", true, start, "/talk/b#message-1");
        var fromYou = new SharedFile(Guid.NewGuid(), "b.txt", "text/plain", 10, "/files/b", false, start.AddDays(-1), "/talk/b/side/s#message-2", Preview: "hi");
        var panel = new FilesPanelViewModel { State = FilesPanelState.Ready, BotName = "Marshal", Files = [fromBot, fromYou] };

        panel.ListName.Should().Be("Files exchanged with Marshal");
        panel.Shown.Select(file => panel.Origin(file).Split(',')[0].Trim()).Should().Equal("From Marshal", "From you");
        panel.Shown.Select(file => (FilesPanelViewModel.OpenMoment, file.MomentHref))
            .Should().Equal(("Open that moment", "/talk/b#message-1"), ("Open that moment", "/talk/b/side/s#message-2"));
        panel.Shown.Select(FilesPanelViewModel.OpenMomentName).Should().Equal("Open that moment: a.pdf", "Open that moment: b.txt");

        panel.From = panel.Sides[^1].Value;

        panel.Shown.Should().ContainSingle().Which.Name.Should().Be("b.txt");
    }

    [Fact]
    public void Each_state_of_the_files_panel_says_what_is_happening()
    {
        var panel = new FilesPanelViewModel { State = FilesPanelState.Ready, BotName = "Marshal" };

        FilesPanelViewModel.Loading.Should().Be("Loading the files.");
        panel.EmptyHint.Should().Be("No files yet. Files you attach and files Marshal shares are listed here.");
    }

    [Fact]
    public void A_files_panel_that_failed_says_why_and_offers_to_try_again()
    {
        var panel = new FilesPanelViewModel { State = FilesPanelState.Failed, BotName = "Marshal", Problem = "The server did not answer." };

        FilesPanelViewModel.FailedTitle.Should().Be("Drover could not load the files");
        FilesPanelViewModel.TryAgain.Should().Be("Try again");
        panel.FailedCause.Should().Be("The server did not answer.");
        new FilesPanelViewModel { State = FilesPanelState.Failed }.FailedCause.Should().Be("The server did not answer.", "a failure always says something");
    }

    #endregion

    #region Exchanged Files

    [Fact]
    public async Task The_files_a_person_and_a_bot_exchanged_are_read_from_the_server_with_where_each_is_and_its_moment()
    {
        var fileId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000f1");
        var threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");
        var messageId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1");
        bench.Server.Answer("GET", $"{ChatBench.Space}/bots/{botId}/files", new[]
        {
            new ExchangedFileResponse(new FileResponse(fileId, "report.pdf", "application/pdf", FileKinds.Other, 10), threadId, messageId, InMainConversation: true, FromBot: true, start),
        });
        var files = bench.Make<ExchangedFilesViewModel>();
        files.Bot = marshal;

        files.State.Should().Be(FilesPanelState.Loading);
        await files.ParametersSet();

        files.State.Should().Be(FilesPanelState.Ready);
        var file = files.Files.Should().ContainSingle().Subject;
        file.Href.Should().Be($"https://drover.test{ChatBench.Space}/files/{fileId}");
        file.MomentHref.Should().Be(Addresses.Moment(botId, threadId, messageId, true));
        file.FromBot.Should().BeTrue();
    }

    [Fact]
    public async Task Files_that_fail_to_load_say_why_load_on_a_second_try_and_are_read_again_only_when_the_conversation_moved_on()
    {
        var address = $"{ChatBench.Space}/bots/{botId}/files";
        bench.Server.Answer("GET", address, HttpStatusCode.InternalServerError);
        var files = bench.Make<ExchangedFilesViewModel>();
        files.Bot = marshal;

        await files.ParametersSet();
        files.State.Should().Be(FilesPanelState.Failed);
        files.Problem.Should().NotBeNullOrWhiteSpace();

        bench.Server.Answer("GET", address, Array.Empty<ExchangedFileResponse>());
        await files.Load();
        files.State.Should().Be(FilesPanelState.Ready);
        files.Problem.Should().BeNull();

        await files.ParametersSet();
        bench.Server.Sent("GET", address).Should().HaveCount(2, "the same version is not read twice");
        files.Version = 1;
        await files.ParametersSet();
        bench.Server.Sent("GET", address).Should().HaveCount(3);
    }

    #endregion

    #region Pasted Pictures

    [Fact]
    public void A_pasted_picture_is_named_for_when_it_was_pasted()
    {
        FileWords.PastedPictureName(new(2026, 11, 20, 14, 3, 59, TimeSpan.FromHours(-5)))
            .Should().Be("Pasted image 2026-11-20 14.03.png");
        FileWords.PastedPictureName(new(2026, 1, 5, 8, 7, 0, TimeSpan.Zero))
            .Should().Be("Pasted image 2026-01-05 08.07.png");
        FileWords.PastedPictureName(new(2026, 1, 5, 0, 0, 0, TimeSpan.FromHours(9)))
            .Should().Be("Pasted image 2026-01-05 00.00.png", "the local clock is what is read, not UTC");
    }

    [Fact]
    public void A_pasted_file_keeps_its_own_name_even_when_it_is_image_png()
    {
        var local = new DateTimeOffset(2026, 11, 20, 14, 3, 0, TimeSpan.Zero);

        FileWords.PastedFileName("image.png", madeForThePaste: false, local)
            .Should().Be("image.png", "a file copied from storage keeps the name it has there");
        FileWords.PastedFileName("Quarterly figures.png", madeForThePaste: false, local)
            .Should().Be("Quarterly figures.png");
    }

    [Fact]
    public void A_picture_made_for_the_paste_or_with_no_name_is_named_for_when_it_was_pasted()
    {
        var local = new DateTimeOffset(2026, 11, 20, 14, 3, 0, TimeSpan.Zero);

        FileWords.PastedFileName("image.png", madeForThePaste: true, local)
            .Should().Be("Pasted image 2026-11-20 14.03.png", "a screenshot has no name, only the one the device gave it");
        FileWords.PastedFileName(" ", madeForThePaste: false, local).Should().Be("Pasted image 2026-11-20 14.03.png");
    }

    #endregion

    #region Private Methods

    private static FileCardViewModel Card(SharedFile file, Func<SharedFile, Task<FileText>>? readWhole = null) =>
        new(NullLogger<FileCardViewModel>.Instance) { File = file, ReadWhole = readWhole };

    #endregion

    #region Nested Types

    private sealed class SampleDelivery : IFileDelivery
    {
        #region Properties

        public SharedFile? Saved { get; private set; }

        public SharedFile? Shared { get; private set; }

        public bool Fails { get; init; }

        /// <summary>What a save or share throws, when it is set.</summary>
        public Exception? Failure { get; set; }

        /// <summary>A save or share waits for this before it ends, when it is set.</summary>
        public Task? Hold { get; init; }

        public int SaveCalls { get; private set; }

        #endregion

        #region Interface Implementations

        public async Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (Hold is not null)
            {
                await Hold;
            }

            Saved = Failure is not null ? throw Failure : Fails ? throw new IOException("Sample: the device refused the file.") : file;
            return true;
        }

        public async Task Share(SharedFile file, CancellationToken cancellationToken = default)
        {
            if (Hold is not null)
            {
                await Hold;
            }

            Shared = Failure is not null ? throw Failure : Fails ? throw new IOException("Sample: the device refused the file.") : file;
        }

        #endregion
    }

    #endregion
}
