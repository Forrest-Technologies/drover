namespace Drover.Ui.Tests.Chat;

/// <summary>
/// Files in the conversation and on the message being written, as components: what is rendered, what each control
/// is named for assistive technology, and what is handed back. The tray and the card moved here from the starters.
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
