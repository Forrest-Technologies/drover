namespace Drover.Ui.Tests.Chat;

/// <summary>
/// Files in the conversation and on the message being written, as components: what is rendered, what each control
/// is named for assistive technology, and what is handed back. The tray, the card, and the files panel moved here from the starters.
/// </summary>
public sealed class FileComponentTests : BunitContext
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Constructors

    public FileComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Script = JSInterop.SetupModule("./_content/Drover.Ui/Chat/Composer.razor.js");
        Script.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region Properties

    private BunitJSModuleInterop Script { get; }

    #endregion

    #region Tray And Card

    [Fact]
    public void A_refused_file_says_why_and_every_file_can_be_taken_off_by_name()
    {
        Guid? removed = null;
        AttachmentItem[] items =
        [
            new(Guid.NewGuid(), "notes.txt", 840, UploadPhase.Ready, 100),
            new(Guid.NewGuid(), "photo.png", 9000, UploadPhase.Ready, 100, FileRefusal.ModelCannotReadImages),
        ];
        var tray = Render<AttachmentTray>(set => set
            .Add(view => view.Items, items)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.OnRemove, id => removed = id));

        tray.FindAll(".dv-attached-problem").Should().ContainSingle().Which.TextContent.Should().Contain("Sample bot's model cannot read pictures.");
        tray.Find("[role=status]").TextContent.Should().Be("1 file cannot be sent.");
        tray.Find("[aria-label='Remove photo.png']").Click();

        removed.Should().Be(items[1].Id);
        Render<AttachmentTray>(set => set.Add(view => view.Items, []).Add(view => view.BotName, "Sample bot")).Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void A_picture_has_its_dimensions_and_every_file_can_be_downloaded_by_name()
    {
        var picture = new SharedFile(Guid.NewGuid(), "chart.png", "image/png", 9000, "/files/1", true, start, Width: 640, Height: 480);
        var card = Render<FileCard>(set => set.Add(view => view.File, picture));

        card.Find("img").GetAttribute("width").Should().Be("640");
        card.Find("img").GetAttribute("height").Should().Be("480");
        card.Find("img").GetAttribute("alt").Should().Be("chart.png");
        card.Find("a[download]").GetAttribute("aria-label").Should().Be("Download chart.png");
        card.Find(".dv-file-facts").TextContent.Should().Be("PNG image, 8.8 KB");
    }

    #endregion

    #region Composer

    [Fact]
    public void Attach_appears_only_after_the_plus_button_which_says_whether_it_is_open()
    {
        var composer = RenderComposer();
        var plus = composer.Find("button[aria-label='More options']");

        plus.GetAttribute("aria-expanded").Should().Be("false");
        composer.FindAll("button.dv-button").Select(button => button.TextContent.Trim()).Should().NotContain("Attach");

        plus.Click();

        composer.Find("button[aria-label='More options']").GetAttribute("aria-expanded").Should().Be("true");
        var options = composer.Find("[role=group][aria-label='Message options']");
        options.Id.Should().Be(composer.Find("button[aria-label='More options']").GetAttribute("aria-controls"));
        options.QuerySelector("button")!.TextContent.Trim().Should().Be("Attach");
        options.QuerySelector("button")!.GetAttribute("type").Should().Be("button");
    }

    [Fact]
    public void Attach_opens_the_file_chooser_through_the_composers_script()
    {
        var composer = RenderComposer();
        composer.Find("button[aria-label='More options']").Click();

        composer.Find("[aria-label='Message options'] button").Click();

        Script.Invocations.Select(call => call.Identifier).Should().Contain("pick");
    }

    [Fact]
    public void Opening_the_options_puts_focus_on_attach_and_attach_waits_for_the_composers_script()
    {
        var composer = RenderComposer();

        composer.Find("button[aria-label='More options']").Click();

        Script.Invocations.Should().Contain(call => call.Identifier == "focusFirst");
        composer.Find("[aria-label='Message options'] button").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Files_chosen_with_attach_are_handed_to_the_conversation()
    {
        List<IBrowserFile> chosen = [];
        var composer = RenderComposer(onFiles: files => chosen.AddRange(files));

        composer.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("one", "a.txt"),
            InputFileContent.CreateFromText("two", "b.md"));

        chosen.Select(file => file.Name).Should().Equal("a.txt", "b.md");
        composer.Find("input[type=file]").HasAttribute("hidden").Should().BeTrue("the input is reached through Attach, paste, and drop");
    }

    [Fact]
    public void Send_waits_until_every_file_is_uploaded_and_needs_no_words_when_there_are_files()
    {
        var item = new AttachmentItem(Guid.NewGuid(), "a.txt", 10, UploadPhase.Uploading, 40);
        List<string> sent = [];
        var composer = RenderComposer(attachments: [item], onSend: sent.Add);

        composer.Find("button[type=submit]").HasAttribute("disabled").Should().BeTrue();
        composer.Find(".dv-attached-status").TextContent.Should().Be("Uploading, 40 percent");

        composer.Render(set => set.Add(view => view.Attachments, [item with { Phase = UploadPhase.Ready, Percent = 100 }]));
        composer.Find("button[type=submit]").HasAttribute("disabled").Should().BeFalse();
        composer.Find("form").Submit();

        sent.Should().Equal(string.Empty);
    }

    [Fact]
    public void A_failed_or_refused_file_holds_the_message_and_says_why_and_its_remove_button_hands_it_back()
    {
        var failed = new AttachmentItem(Guid.NewGuid(), "a.txt", 10, UploadPhase.Failed, 0);
        var refused = new AttachmentItem(Guid.NewGuid(), "big.bin", 10, UploadPhase.Ready, 100, FileRefusal.TooLarge);
        List<Guid> removed = [];
        var composer = RenderComposer(attachments: [failed, refused], onRemove: removed.Add);
        composer.Find("textarea").Input("Here");

        composer.Find("button[type=submit]").HasAttribute("disabled").Should().BeTrue();
        composer.FindAll(".dv-attached-status").Select(status => status.TextContent).Should().Equal(
            "Not uploaded. Remove it and attach it again.", "Larger than this server takes.");
        composer.Find("[aria-label='Remove a.txt']").Click();

        removed.Should().Equal(failed.Id);
    }

    #endregion

    #region Conversation

    [Fact]
    public void A_persons_files_show_under_their_words_with_the_same_way_to_download_each()
    {
        var picture = new SharedFile(Guid.NewGuid(), "cat.png", "image/png", 2048, "/api/files/cat", false, start, Width: 64, Height: 48);
        var text = new SharedFile(Guid.NewGuid(), "notes.txt", "text/plain", 12, "/api/files/notes", false, start, Preview: "first line");
        var message = new ChatMessage("m1", true, string.Empty) { Files = [picture, text] };

        var list = Render<MessageList>(set => set
            .Add(view => view.Messages, [message])
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.BotHue, 2));

        list.FindAll(".dv-message-bubble").Should().BeEmpty("a message with only files has no words to show");
        list.Find("img").GetAttribute("src").Should().Be("/api/files/cat");
        (list.Find("img").GetAttribute("width"), list.Find("img").GetAttribute("height")).Should().Be(("64", "48"));
        list.Find("[role=group][aria-label='The first lines of notes.txt'] pre").TextContent.Should().Be("first line");
        list.FindAll("a[download]").Select(link => link.GetAttribute("aria-label")).Should().Equal("Download cat.png", "Download notes.txt");
    }

    [Fact]
    public void A_file_a_bot_shared_shows_in_its_reply_with_its_caption_and_the_message_can_be_linked_to()
    {
        var report = new SharedFile(Guid.NewGuid(), "report.pdf", "application/pdf", 3_400_000, "/api/files/report", true, start, Caption: "The weekly numbers");
        var id = Guid.NewGuid();
        var reply = new ChatMessage(id.ToString(), false, "Here it is.") { Files = [report] };

        var list = Render<MessageList>(set => set
            .Add(view => view.Messages, [reply])
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Moment, id));

        var article = list.Find("article.dv-message-bot");
        article.GetAttribute("id").Should().Be($"message-{id}");
        article.GetAttribute("tabindex").Should().Be("-1", "the message a link names can take focus");
        article.QuerySelector("figure.dv-file .dv-file-facts")!.TextContent.Should().Be("PDF document, 3.2 MB");
        article.QuerySelector(".dv-file-caption")!.TextContent.Should().Be("The weekly numbers");
        article.QuerySelector("a[download]")!.GetAttribute("aria-label").Should().Be("Download report.pdf");
    }

    [Fact]
    public void A_moment_link_followed_again_after_leaving_lands_again()
    {
        var module = JSInterop.SetupModule("./_content/Drover.Ui/Chat/MessageList.razor.js");
        module.SetupVoid("showMoment", _ => true).SetVoidResult();
        var id = Guid.NewGuid();
        var reply = new ChatMessage(id.ToString(), false, "Here it is.");
        var list = Render<MessageList>(set => set.Add(view => view.Messages, [reply]).Add(view => view.BotName, "Sample bot").Add(view => view.Moment, id));
        list.WaitForAssertion(() => module.VerifyInvoke("showMoment", 1));

        list.Render(set => set.Add(view => view.Moment, (Guid?)null));
        list.Render(set => set.Add(view => view.Moment, id));

        list.WaitForAssertion(() => module.VerifyInvoke("showMoment", 2).Should().AllSatisfy(call => call.Arguments.Should().Equal($"message-{id}")));
    }

    [Fact]
    public void Show_all_brings_a_text_files_whole_content_into_its_box_and_says_when_it_is_cut()
    {
        var longFile = new SharedFile(Guid.NewGuid(), "log.txt", "text/plain", 5_000, "/api/files/log", true, start, Preview: "line one\nline two");
        SharedFile? asked = null;
        var answer = new FileText("line one\nline two\nline three", Cut: false);
        var card = Render<FileCard>(set => set
            .Add(view => view.File, longFile)
            .Add(view => view.ReadWhole, file =>
            {
                asked = file;
                return Task.FromResult(answer);
            }));

        card.Find("button").Click();

        asked.Should().Be(longFile);
        card.Find("[role=group]").GetAttribute("aria-label").Should().Be("All of log.txt");
        card.Find("pre").TextContent.Should().Be("line one\nline two\nline three");
        card.FindAll("button").Should().BeEmpty("everything is shown");

        answer = answer with { Cut = true };
        var cut = Render<FileCard>(set => set.Add(view => view.File, longFile).Add(view => view.ReadWhole, _ => Task.FromResult(answer)));
        cut.Find("button").GetAttribute("aria-label").Should().Be("Show all of log.txt");
        cut.Find("button").Click();
        cut.Find(".dv-file-note").TextContent.Should().Be("Only the first 100,000 characters of log.txt are shown. Download it for the rest.");
    }

    [Fact]
    public void A_text_file_that_shows_all_of_itself_or_a_card_with_no_reader_offers_no_show_all()
    {
        var whole = new SharedFile(Guid.NewGuid(), "short.txt", "text/plain", 19, "/api/files/short", true, start, Preview: "line one\nline two");
        var longer = whole with { Size = 5_000 };

        Render<FileCard>(set => set.Add(view => view.File, whole).Add(view => view.ReadWhole, _ => Task.FromResult(new FileText(string.Empty, false))))
            .FindAll("button").Should().BeEmpty("a file of 17 characters, two line breaks, and a carriage return has nothing more");
        Render<FileCard>(set => set.Add(view => view.File, longer)).FindAll("button").Should().BeEmpty("there is no way to read it");
    }

    [Fact]
    public void Show_all_that_fails_says_so_and_can_be_tried_again()
    {
        var file = new SharedFile(Guid.NewGuid(), "log.txt", "text/plain", 5_000, "/api/files/log", true, start, Preview: "line one");
        var card = Render<FileCard>(set => set.Add(view => view.File, file).Add(view => view.ReadWhole, _ => throw new HttpRequestException("down")));

        card.Find("button").Click();

        card.Find(".dv-note").TextContent.Should().Contain("Drover could not load the rest of log.txt.");
        card.Find("button").GetAttribute("aria-label").Should().Be("Show all of log.txt");
    }

    #endregion

    #region Files Panel

    [Fact]
    public void The_files_filter_appears_only_when_files_came_from_both_sides()
    {
        var fromBot = new SharedFile(Guid.NewGuid(), "a.pdf", "application/pdf", 10, "/files/a", true, start);
        var panel = Render<FilesPanel>(set => set.Add(view => view.State, FilesPanelState.Ready).Add(view => view.BotName, "Sample bot").Add(view => view.Files, [fromBot]));

        panel.FindAll("[role=radiogroup], .dv-segmented").Should().BeEmpty();
        panel.Find(".dv-files-origin").TextContent.Should().Contain("From Sample bot");

        panel.Render(set => set.Add(view => view.Files, [fromBot, fromBot with { Id = Guid.NewGuid(), FromBot = false }]));

        panel.Markup.Should().Contain("Whose files");
    }

    [Fact]
    public void The_files_panel_lists_each_file_with_who_it_came_from_when_and_a_link_to_that_moment()
    {
        var fromBot = new SharedFile(Guid.NewGuid(), "a.pdf", "application/pdf", 10, "/files/a", true, start, "/talk/b#message-1");
        var fromYou = new SharedFile(Guid.NewGuid(), "b.txt", "text/plain", 10, "/files/b", false, start.AddDays(-1), "/talk/b/side/s#message-2", Preview: "hi");
        var panel = Render<FilesPanel>(set => set
            .Add(view => view.State, FilesPanelState.Ready)
            .Add(view => view.BotName, "Marshal")
            .Add(view => view.Files, [fromBot, fromYou]));

        panel.Find("ul").GetAttribute("aria-label").Should().Be("Files exchanged with Marshal");
        panel.FindAll(".dv-files-origin").Select(line => line.TextContent.Split(',')[0].Trim()).Should().Equal("From Marshal", "From you");
        panel.FindAll(".dv-files-origin a").Select(link => (link.TextContent, link.GetAttribute("href")))
            .Should().Equal(("Open that moment", "/talk/b#message-1"), ("Open that moment", "/talk/b/side/s#message-2"));
        panel.FindAll(".dv-files-origin a").Select(link => link.GetAttribute("aria-label"))
            .Should().Equal("Open that moment: a.pdf", "Open that moment: b.txt");

        panel.FindAll("input[type=radio]")[^1].Change(true);

        panel.FindAll("li").Should().ContainSingle().Which.TextContent.Should().Contain("b.txt");
    }

    [Theory]
    [InlineData(FilesPanelState.Loading, "Loading the files.")]
    [InlineData(FilesPanelState.Ready, "No files yet. Files you attach and files Marshal shares are listed here.")]
    public void Each_state_of_the_files_panel_says_what_is_happening(FilesPanelState state, string sentence)
    {
        var panel = Render<FilesPanel>(set => set.Add(view => view.State, state).Add(view => view.BotName, "Marshal"));

        panel.Find(".dv-files-hint").TextContent.Should().Be(sentence);
    }

    [Fact]
    public void A_files_panel_that_failed_says_why_and_tries_again()
    {
        var retried = false;
        var panel = Render<FilesPanel>(set => set
            .Add(view => view.State, FilesPanelState.Failed)
            .Add(view => view.BotName, "Marshal")
            .Add(view => view.Problem, "The server did not answer.")
            .Add(view => view.OnRetry, () => retried = true));

        panel.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the files");
        panel.Find(".dv-empty button").TextContent.Trim().Should().Be("Try again");
        panel.Find(".dv-empty button").Click();

        retried.Should().BeTrue();
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Composer> RenderComposer(
        IReadOnlyList<AttachmentItem>? attachments = null,
        Action<string>? onSend = null,
        Action<IReadOnlyList<IBrowserFile>>? onFiles = null,
        Action<Guid>? onRemove = null) =>
        Render<Composer>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Attachments, attachments ?? [])
            .Add(view => view.OnSend, onSend ?? (_ => { }))
            .Add(view => view.OnFiles, onFiles ?? (_ => { }))
            .Add(view => view.OnRemoveFile, onRemove ?? (_ => { })));

    #endregion
}
