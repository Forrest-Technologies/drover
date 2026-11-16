namespace Drover.Ui.Tests.Chat;

/// <summary>Files in words, and the files chosen for a message. Moved here from the starters with #151.</summary>
public sealed class FileWordsTests
{
    #region Private Fields

    private static readonly AttachmentRules rules = new(MaxFileBytes: 1000, MaxFilesPerMessage: 2, ModelReadsImages: false, HasSandbox: false);

    #endregion

    #region Words

    [Fact]
    public void A_file_is_named_by_its_kind_and_a_refused_one_says_why()
    {
        FileWords.KindName("application/pdf", "report.pdf").Should().Be("PDF document");
        FileWords.KindName("image/png", "photo.png").Should().Be("PNG image");
        FileWords.KindName("application/octet-stream", "data.parquet").Should().Be("PARQUET file");
        FileWords.KindName("application/octet-stream", "data").Should().Be("File");

        var ready = new AttachmentItem(Guid.NewGuid(), "a.txt", 840, UploadPhase.Ready, 100);
        var uploading = ready with { Phase = UploadPhase.Uploading, Percent = 40 };
        var refused = ready with { Refusal = FileRefusal.ModelCannotReadImages };

        FileWords.Status(ready, "Sample bot").Should().Be("840 bytes");
        FileWords.Status(uploading, "Sample bot").Should().Be("Uploading, 40 percent");
        FileWords.Status(refused, "Sample bot").Should().Be("Sample bot's model cannot read pictures.");
        FileWords.CanSend([ready]).Should().BeTrue();
        FileWords.CanSend([ready, uploading]).Should().BeFalse();
        FileWords.CanSend([ready, refused]).Should().BeFalse();
    }

    [Theory]
    [InlineData(FileRefusals.TooLarge, FileRefusal.TooLarge)]
    [InlineData(FileRefusals.TooMany, FileRefusal.TooMany)]
    [InlineData(FileRefusals.SpaceFull, FileRefusal.SpaceFull)]
    [InlineData(FileRefusals.ModelCannotReadImages, FileRefusal.ModelCannotReadImages)]
    [InlineData(FileRefusals.NothingCanOpenIt, FileRefusal.NothingCanOpenIt)]
    [InlineData(FileRefusals.PrivateConversation, FileRefusal.PrivateConversation)]
    [InlineData("unheard_of", FileRefusal.None)]
    public void Each_refusal_the_server_names_is_said_in_the_composers_words(string code, FileRefusal refusal) =>
        FileWords.RefusalFrom(code).Should().Be(refusal);

    [Fact]
    public void An_upload_whose_progress_cannot_be_measured_says_uploading_without_a_number()
    {
        var item = new AttachmentItem(Guid.NewGuid(), "a.txt", 10, UploadPhase.Uploading, 0);

        FileWords.Status(item, "Sample bot").Should().Be("Uploading");
    }

    [Fact]
    public void A_full_space_is_said_without_blaming_the_file()
    {
        var item = new AttachmentItem(Guid.NewGuid(), "a.txt", 10, UploadPhase.Ready, 100, FileRefusal.SpaceFull);

        FileWords.Status(item, "Sample bot").Should().Be("This space has no room left for files on this server.");
    }

    [Fact]
    public void A_file_refused_in_a_private_conversation_says_what_failed_and_why()
    {
        var item = new AttachmentItem(Guid.NewGuid(), "report.pdf", 10, UploadPhase.Ready, 100, FileRefusal.PrivateConversation);

        FileWords.Status(item, "Sample bot").Should().Be(
            "This conversation is private, and others in this space can reach Sample bot's sandbox, so this file cannot go there.");
    }

    [Fact]
    public void A_stored_file_becomes_a_card_with_its_address_size_and_first_lines()
    {
        var stored = new FileResponse(Guid.NewGuid(), "notes.txt", "text/plain", FileKinds.Text, 12, Preview: "first line");

        var shared = FileWords.Shared(stored, "/files/1", fromBot: false);

        shared.Should().BeEquivalentTo(new { stored.Id, Name = "notes.txt", ContentType = "text/plain", Size = 12L, Href = "/files/1", FromBot = false, Preview = "first line" });
        FileWords.Kind(shared).Should().Be(FileKind.Text);
    }

    #endregion

    #region Draft

    [Fact]
    public void A_chosen_file_uploads_says_how_far_it_got_and_is_ready_once_stored()
    {
        var draft = new AttachmentDraft();

        var item = draft.Add("a.txt", 400, rules);
        draft.Progress(item.Id, 100).Should().BeTrue();
        draft.Progress(item.Id, 100).Should().BeFalse("the words did not change");

        draft.Items.Single().Should().Match<AttachmentItem>(shown => shown.Phase == UploadPhase.Uploading && shown.Percent == 25);
        draft.CanSend.Should().BeFalse();

        var stored = Guid.NewGuid();
        draft.Uploaded(item.Id, new FileResponse(stored, "a.txt", "text/plain", FileKinds.Text, 400)).Should().BeTrue();

        draft.Items.Single().Phase.Should().Be(UploadPhase.Ready);
        draft.CanSend.Should().BeTrue();
        draft.FileIds.Should().Equal(stored);
    }

    [Fact]
    public void A_file_past_the_limits_is_refused_before_any_of_it_is_sent()
    {
        var draft = new AttachmentDraft();

        var large = draft.Add("big.bin", 1001, rules);
        draft.Add("a.txt", 1, rules);
        draft.Add("b.txt", 1, rules);
        var third = draft.Add("c.txt", 1, rules);

        large.Refusal.Should().Be(FileRefusal.TooLarge);
        third.Refusal.Should().Be(FileRefusal.TooMany, "a refused file does not count toward the message's files");
        draft.Items.Count(shown => shown.Phase is UploadPhase.Uploading).Should().Be(2);
    }

    [Fact]
    public void A_refusal_or_failure_from_the_server_holds_the_message_until_the_file_is_taken_off()
    {
        var draft = new AttachmentDraft();
        var refused = draft.Add("photo.png", 10, rules);
        var failed = draft.Add("a.txt", 10, rules);

        draft.Refused(refused.Id, FileRefusal.ModelCannotReadImages);
        draft.Failed(failed.Id);

        draft.Items.Select(shown => (shown.Phase, shown.Refusal)).Should().Equal(
            (UploadPhase.Ready, FileRefusal.ModelCannotReadImages), (UploadPhase.Failed, FileRefusal.None));
        draft.CanSend.Should().BeFalse();

        draft.Remove(refused.Id).Should().BeNull("it never reached the server");
        draft.Remove(failed.Id);
        draft.CanSend.Should().BeTrue();
        draft.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_file_taken_off_while_on_its_way_says_so_when_it_arrives()
    {
        var draft = new AttachmentDraft();
        var item = draft.Add("a.txt", 10, rules);
        var stored = Guid.NewGuid();

        draft.Remove(item.Id);

        draft.Uploaded(item.Id, new FileResponse(stored, "a.txt", "text/plain", FileKinds.Text, 10)).Should().BeFalse();
        draft.FileIds.Should().BeEmpty();
    }

    [Fact]
    public void Taking_off_a_stored_file_hands_back_its_server_id_and_sending_clears_the_rest()
    {
        var draft = new AttachmentDraft();
        var first = draft.Add("a.txt", 10, rules);
        var second = draft.Add("b.txt", 10, rules);
        var stored = Guid.NewGuid();
        draft.Uploaded(first.Id, new FileResponse(stored, "a.txt", "text/plain", FileKinds.Text, 10));

        draft.Remove(first.Id).Should().Be(stored);
        draft.Clear();

        draft.Items.Should().BeEmpty();
        draft.Uploaded(second.Id, new FileResponse(Guid.NewGuid(), "b.txt", "text/plain", FileKinds.Text, 10)).Should().BeFalse();
    }

    [Fact]
    public void A_failed_file_holds_no_place_on_the_message()
    {
        var draft = new AttachmentDraft();
        var failed = draft.Add("a.txt", 1, rules);
        draft.Failed(failed.Id);
        draft.Add("b.txt", 1, rules);

        draft.Add("c.txt", 1, rules).Refusal.Should().Be(FileRefusal.None);
    }

    [Fact]
    public void Sending_takes_off_only_the_files_that_went()
    {
        var draft = new AttachmentDraft();
        var sent = draft.Add("a.txt", 1, rules);
        var later = draft.Add("b.txt", 1, rules);
        var sentId = Guid.NewGuid();
        draft.Uploaded(sent.Id, new FileResponse(sentId, "a.txt", "text/plain", FileKinds.Text, 1));

        draft.Sent([sentId]);

        draft.Items.Select(item => item.Id).Should().Equal(later.Id);
    }

    #endregion
}
