namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// One project's screen against a stand-in server: who may edit, which bot a conversation starts with, and what a
/// move sends. Every name here is a sample.
/// </summary>
public sealed class ProjectViewModelTests : ViewModelTest
{
    #region Private Fields

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly Guid projectId = Guid.Parse("0196f1a0-0000-7000-8000-000000000010");

    private static readonly Guid otherProjectId = Guid.Parse("0196f1a0-0000-7000-8000-000000000019");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000011");

    private static readonly Guid pausedBotId = Guid.Parse("0196f1a0-0000-7000-8000-000000000012");

    private static readonly Guid thirdBotId = Guid.Parse("0196f1a0-0000-7000-8000-000000000017");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000013");

    private static readonly Guid outsideId = Guid.Parse("0196f1a0-0000-7000-8000-000000000014");

    private static readonly Guid fileId = Guid.Parse("0196f1a0-0000-7000-8000-000000000015");

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000016");

    private static readonly DateTimeOffset when = DateTimeOffset.Parse("2026-01-02T03:04:05Z");

    private static readonly string projectPath = $"{space}/projects/{projectId}";

    private static readonly string threadPath = $"{projectPath}/threads";

    private static readonly string filePath = $"{projectPath}/files";

    private const long HostedMaxBytes = 10L * 1024 * 1024;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_project_shows_only_the_conversations_and_files_the_server_returned()
    {
        var inside = Thread(threadId, botId, "Weekly notes", projectId);
        var outside = Thread(outsideId, botId, "Side notes", null);
        var screen = Make<ProjectViewModel>();
        Answer(threads: [inside], files: [Note], bots: [Marshal, Paused], own: [outside, inside]);

        await screen.Open(projectId);

        screen.State.Should().Be(ProjectScreenState.Ready);
        screen.Name.Should().Be("Weekly");
        screen.Instructions.Should().Be("Ship the note.");
        screen.DraftName.Should().Be("Weekly");
        screen.DraftInstructions.Should().Be("Ship the note.");
        screen.CanManage.Should().BeTrue();
        screen.Delivers.Should().BeFalse();
        screen.Threads.Select(thread => thread.Id).Should().Equal(threadId);
        screen.Movable.Select(thread => thread.Id).Should().Equal(outsideId);
        screen.Files.Should().ContainSingle().Which.File.Name.Should().Be("note.txt");
        screen.ThreadLabel(inside).Should().Be("Weekly notes");
        screen.ThreadLabel(Thread(threadId, botId, null, projectId)).Should().Be("Conversation with Marshal");
        screen.DownloadAddress(fileId).Should().Contain($"/projects/{projectId}/files/{fileId}?download=true");
    }

    [Fact]
    public async Task Opening_reads_owned_conversations_in_one_request_even_with_many_bots()
    {
        var outside = Thread(outsideId, thirdBotId, "Side notes", null);
        var thirdBot = new BotSummary(thirdBotId, "Scout", "scout", "Stay brief.", connectionId, "sample", "active");
        var screen = Make<ProjectViewModel>();
        Answer(bots: [Marshal, Paused, thirdBot], own: [outside]);

        await screen.Open(projectId);

        screen.Movable.Select(thread => thread.Id).Should().Equal(outsideId);
        Server.Requests.Count(request => request.Method == "GET" && request.Path == $"{space}/threads").Should().Be(1);
        Server.Requests.Should().NotContain(request => request.Path.EndsWith("/threads", StringComparison.Ordinal) && request.Path.Contains("/bots/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_member_sees_editing_unavailable_and_can_still_start_and_move()
    {
        var outside = Thread(outsideId, botId, "Side notes", null);
        var screen = Make<ProjectViewModel>();
        Answer(role: "member", bots: [Marshal], own: [outside]);
        await screen.Open(projectId);
        var created = Thread(threadId, botId, null, projectId);
        Server.Answer("POST", $"{space}/bots/{botId}/threads", created);
        Server.Answer("PUT", $"{space}/threads/{outsideId}/project", outside with { ProjectId = projectId });

        screen.CanManage.Should().BeFalse();
        ProjectViewModel.MemberNote.Should().Contain("Owners and admins");
        await screen.Save("Renamed", "Version two.");
        await screen.Upload(new SampleFile("note.txt", 4, [1, 2, 3, 4]));
        await screen.RemoveFile(Note);
        screen.PrepareRemove();
        await screen.Remove();
        await screen.Start(botId);
        await screen.MoveIn(outsideId);

        screen.Problem.Should().BeNull();
        Server.Sent("PUT", projectPath).Should().BeEmpty();
        Server.Sent("POST", filePath).Should().BeEmpty();
        Server.Sent("DELETE", projectPath).Should().BeEmpty();
        Server.Sent("DELETE", $"{filePath}/{fileId}").Should().BeEmpty();
        Server.Sent("POST", $"{space}/bots/{botId}/threads").Single().Should().Contain(projectId.ToString());
        Server.Sent("PUT", $"{space}/threads/{outsideId}/project").Single().Should().Contain(projectId.ToString());
        Navigator.Current.Should().Be(Addresses.Conversation(threadId));
    }

    [Fact]
    public async Task An_owner_saves_the_fields_they_typed_and_a_blank_name_is_not_sent()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        await screen.Save("   ", "Version two.");

        screen.Problem.Should().Be(ProjectViewModel.NeedsName);
        Server.Sent("PUT", projectPath).Should().BeEmpty();

        var renamed = Weekly with { Name = "Renamed", Instructions = "Version two." };
        Server.Answer("GET", projectPath, renamed);
        Server.Answer("PUT", projectPath, renamed);
        await screen.Save("  Renamed  ", "Version two.");

        screen.Name.Should().Be("Renamed");
        screen.Instructions.Should().Be("Version two.");
        screen.DraftName.Should().Be("Renamed");
        screen.DraftInstructions.Should().Be("Version two.");
        screen.Problem.Should().BeNull();
        Server.Sent("PUT", projectPath).Single().Should().Contain("Renamed").And.Contain("Version two").And.NotContain("  Renamed");
    }

    [Fact]
    public async Task Editing_starts_from_the_stored_name_and_instructions()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);

        screen.StartEditing();

        screen.Editing.Should().BeTrue();
        screen.DraftName.Should().Be("Weekly");
        screen.DraftInstructions.Should().Be("Ship the note.");
        screen.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_an_unchanged_edit_closes_at_once()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        screen.StartEditing();

        screen.CancelEditing();

        screen.Editing.Should().BeFalse();
        screen.ConfirmingDiscard.Should().BeFalse();
        screen.DraftName.Should().Be("Weekly");
    }

    [Fact]
    public async Task Cancelling_a_changed_edit_asks_before_discarding()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        screen.StartEditing();
        screen.UpdateDraftName("Renamed");
        screen.UpdateDraftInstructions("New words.");
        ProjectViewModel.DiscardQuestion("Weekly").Should().Be("Your changes to Weekly are not saved.");

        screen.CancelEditing();

        screen.ConfirmingDiscard.Should().BeTrue();
        screen.Editing.Should().BeTrue();

        screen.KeepEditing();
        screen.ConfirmingDiscard.Should().BeFalse();
        screen.Editing.Should().BeTrue();

        screen.CancelEditing();
        screen.Discard();

        screen.ConfirmingDiscard.Should().BeFalse();
        screen.Editing.Should().BeFalse();
        screen.DraftName.Should().Be("Weekly");
        screen.DraftInstructions.Should().Be("Ship the note.");
    }

    [Fact]
    public async Task A_saved_edit_closes_the_sheet_and_a_refused_one_keeps_it_open_with_the_reason()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        screen.StartEditing();
        screen.UpdateDraftName("Renamed");
        screen.UpdateDraftInstructions("New words.");
        var saved = Weekly with { Name = "Renamed", Instructions = "New words." };
        Server.Answer("PUT", projectPath, saved);
        Server.Answer("GET", projectPath, saved);

        await screen.Save();

        screen.Editing.Should().BeFalse();
        screen.Problem.Should().BeNull();

        screen.StartEditing();
        screen.UpdateDraftName("Refused");
        Server.Refuse("PUT", projectPath, HttpStatusCode.Forbidden, "Only an owner or admin can change a project.");

        await screen.Save();

        screen.Editing.Should().BeTrue();
        screen.Problem.Should().Be("Only an owner or admin can change a project.");
    }

    [Fact]
    public async Task Closing_the_sheet_clears_the_last_error()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        screen.StartEditing();
        screen.UpdateDraftName(string.Empty);

        await screen.Save();

        screen.Problem.Should().Be(ProjectViewModel.NeedsName);
        screen.Discard();
        screen.Problem.Should().BeNull();

        screen.StartEditing();
        screen.Problem = "The last refusal.";
        screen.CancelEditing();
        screen.Problem.Should().BeNull();
    }

    [Fact]
    public async Task A_member_cannot_open_the_edit_or_remove_file_dialogs()
    {
        var screen = Make<ProjectViewModel>();
        Answer(role: "member", files: [Note]);
        await screen.Open(projectId);

        screen.StartEditing();
        screen.AskRemoveFile(Note);

        screen.Editing.Should().BeFalse();
        screen.RemovingFile.Should().BeNull();
    }

    [Fact]
    public async Task Removing_a_project_clears_the_confirmation_and_a_refusal_stays_on_the_dialog()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        Server.Refuse("DELETE", projectPath, HttpStatusCode.Forbidden, "Only an owner or admin can remove a project.");

        screen.PrepareRemove();
        await screen.Remove();

        screen.ConfirmingRemove.Should().BeTrue();
        screen.Problem.Should().Be("Only an owner or admin can remove a project.");

        screen.CancelRemove();
        screen.ConfirmingRemove.Should().BeFalse();
        screen.Problem.Should().BeNull();

        screen.PrepareRemove();
        Server.Answer("DELETE", projectPath, HttpStatusCode.NoContent);
        await screen.Remove();

        screen.ConfirmingRemove.Should().BeFalse();
    }

    [Fact]
    public async Task Removing_a_file_asks_first_and_keeping_it_sends_nothing()
    {
        var screen = Make<ProjectViewModel>();
        Answer(files: [Note]);
        await screen.Open(projectId);

        screen.AskRemoveFile(Note);
        screen.RemovingFile.Should().Be(Note);
        screen.KeepFile();

        screen.RemovingFile.Should().BeNull();
        Server.Sent("DELETE", $"{filePath}/{fileId}").Should().BeEmpty();

        screen.AskRemoveFile(Note);
        Server.Answer("DELETE", $"{filePath}/{fileId}", HttpStatusCode.NoContent);
        Server.Answer("GET", filePath, Array.Empty<ProjectFileResponse>());
        await screen.RemoveFile(Note);

        Server.Sent("DELETE", $"{filePath}/{fileId}").Should().ContainSingle();
        screen.RemovingFile.Should().BeNull();
        screen.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task The_move_dialog_lists_only_conversations_that_can_move_in()
    {
        var inside = Thread(threadId, botId, "Weekly notes", projectId);
        var outside = Thread(outsideId, botId, "Side notes", null);
        var screen = Make<ProjectViewModel>();
        Answer(threads: [inside], own: [outside]);
        await screen.Open(projectId);

        screen.StartMoving();

        screen.MovingConversations.Should().BeTrue();
        screen.Movable.Select(thread => thread.Id).Should().Equal(outsideId);
        screen.Movable.Should().NotContain(thread => thread.Id == inside.Id);

        screen.StopMoving();
        screen.MovingConversations.Should().BeFalse();
    }

    [Fact]
    public async Task Edits_made_while_a_save_is_in_flight_remain_in_the_fields()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        screen.UpdateDraftName("Submitted name");
        screen.UpdateDraftInstructions("Submitted instructions");
        var saved = Weekly with { Name = "Submitted name", Instructions = "Submitted instructions" };
        Server.Answer("PUT", projectPath, saved);
        Server.Answer("GET", projectPath, saved);
        var held = Server.Hold("PUT", projectPath);

        var saving = screen.Save();
        screen.UpdateDraftName("Later name");
        screen.UpdateDraftInstructions("Later instructions");
        held.SetResult();
        await saving;

        Server.Sent("PUT", projectPath).Should().ContainSingle()
            .Which.Should().Contain("Submitted name").And.Contain("Submitted instructions");
        screen.Name.Should().Be("Submitted name");
        screen.DraftName.Should().Be("Later name");
        screen.DraftInstructions.Should().Be("Later instructions");
    }

    [Fact]
    public async Task Typed_drafts_survive_reload_upload_removal_and_move_until_saved_or_switching_project()
    {
        var outside = Thread(outsideId, botId, "Side notes", null);
        var screen = Make<ProjectViewModel>();
        Answer(files: [Note], bots: [Marshal], own: [outside]);
        await screen.Open(projectId);

        screen.UpdateDraftName("Edited name");
        screen.UpdateDraftInstructions("Edited instructions");

        Server.Answer("POST", filePath, Note);
        await screen.Upload(new SampleFile("note.txt", 4, [1, 2, 3, 4]));
        screen.DraftName.Should().Be("Edited name");
        screen.DraftInstructions.Should().Be("Edited instructions");

        Server.Answer("DELETE", $"{filePath}/{fileId}", HttpStatusCode.NoContent);
        await screen.RemoveFile(Note);
        screen.DraftName.Should().Be("Edited name");
        screen.DraftInstructions.Should().Be("Edited instructions");

        var moved = outside with { ProjectId = projectId };
        Server.Answer("PUT", $"{space}/threads/{outsideId}/project", moved);
        await screen.MoveIn(outsideId);
        screen.DraftName.Should().Be("Edited name");
        screen.DraftInstructions.Should().Be("Edited instructions");

        var saved = Weekly with { Name = "Edited name", Instructions = "Edited instructions" };
        Server.Answer("PUT", projectPath, saved);
        Server.Answer("GET", projectPath, saved);
        await screen.Save();
        screen.DraftName.Should().Be("Edited name");

        var other = new ProjectSummary(otherProjectId, "Other", "Other instructions.", when, when);
        Server.Answer("GET", $"{space}/projects/{otherProjectId}", other);
        Server.Answer("GET", $"{space}/projects/{otherProjectId}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{space}/projects/{otherProjectId}/files", Array.Empty<ProjectFileResponse>());
        await screen.Open(otherProjectId);

        screen.DraftName.Should().Be("Other");
        screen.DraftInstructions.Should().Be("Other instructions.");
    }

    [Fact]
    public async Task Removing_a_project_asks_first_and_says_conversations_stay()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        ProjectViewModel.RemoveQuestion.Should().Be("Its conversations are kept and leave the project.");
        ProjectViewModel.RemoveProject.Should().Be("Remove project");
        ProjectViewModel.KeepProject.Should().Be("Keep project");

        await screen.Remove();
        screen.PrepareRemove();
        screen.CancelRemove();
        await screen.Remove();
        Server.Sent("DELETE", projectPath).Should().BeEmpty();

        screen.PrepareRemove();
        Server.Answer("DELETE", projectPath, HttpStatusCode.NoContent);
        await screen.Remove();

        Navigator.Current.Should().Be(Addresses.Projects);
    }

    [Fact]
    public async Task A_file_uploads_by_the_servers_limit_and_a_file_past_it_is_not_sent()
    {
        var screen = Make<ProjectViewModel>();
        Answer(maxFileBytes: HostedMaxBytes);
        await screen.Open(projectId);
        var known = new SampleFile("note.txt", 4, [1, 2, 3, 4]);
        var unknown = new SampleFile("scan.bin", 0, [9]);
        var tooBig = new SampleFile("huge.bin", HostedMaxBytes + 1, [1]);
        Server.Answer("POST", filePath, Note);
        Server.Answer("GET", filePath, new[] { Note });

        await screen.Upload(known);
        await screen.Upload(unknown);
        await screen.Upload(tooBig);

        known.RequestedCap.Should().Be(HostedMaxBytes);
        unknown.RequestedCap.Should().Be(HostedMaxBytes);
        tooBig.RequestedCap.Should().Be(0);
        Server.Addresses.Where(address => address.StartsWith($"POST {filePath}", StringComparison.Ordinal)).Should().HaveCount(2);
        Server.Addresses.Should().Contain(address => address.Contains("name=note.txt", StringComparison.Ordinal));
        Server.Addresses.Should().Contain(address => address.Contains("name=scan.bin", StringComparison.Ordinal));
        screen.Problem.Should().Be(screen.TooLarge);
        screen.Problem.Should().Contain("10 MB");
        screen.Files.Should().ContainSingle();
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public async Task A_file_that_cannot_be_read_reports_its_name_without_logging_the_failure(string failureKind)
    {
        const string RawFailure = "raw-reader-failure";
        var failure = failureKind switch
        {
            "io" => (Exception)new IOException(RawFailure),
            _ => new UnauthorizedAccessException(RawFailure),
        };
        var log = new RecordingLogger<ProjectViewModel>();
        var screen = new ProjectViewModel(Server.CreateApi(), Session, Navigator, log);
        var file = new SampleFile("private-note.txt", 4, [1, 2, 3, 4], failure);
        Answer(maxFileBytes: HostedMaxBytes);
        await screen.Open(projectId);

        await screen.Upload(file);

        screen.Problem.Should().Contain(file.Name).And.NotContain(RawFailure);
        screen.Working.Should().BeNull();
        Server.Sent("POST", filePath).Should().BeEmpty();
        log.Entries.Should().ContainSingle();
        log.Entries.Should().OnlyContain(entry =>
            entry.Exception == null && !entry.Message.Contains(RawFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Choosing_files_reports_local_failures_without_logging_the_failure_text()
    {
        const string RawFailure = "raw-chooser-failure";
        var log = new RecordingLogger<ProjectViewModel>();
        var screen = new ProjectViewModel(Server.CreateApi(), Session, Navigator, log);
        Answer();
        await screen.Open(projectId);

        await screen.ChooseFiles(() =>
            Task.FromException<IReadOnlyList<IPickedFile>>(new IOException(RawFailure)));

        screen.Problem.Should().Be("Drover could not open the file picker. Try again.");
        screen.Working.Should().BeNull();
        Server.Sent("POST", filePath).Should().BeEmpty();
        log.Entries.Should().ContainSingle();
        log.Entries.Should().OnlyContain(entry =>
            entry.Exception == null && !entry.Message.Contains(RawFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelling_or_emptying_the_file_chooser_preserves_the_existing_problem()
    {
        var screen = Make<ProjectViewModel>();
        Answer();
        await screen.Open(projectId);
        await screen.Save(" ", "ignored");
        var previous = screen.Problem;
        var cancelled = new CancellationToken(canceled: true);

        await screen.ChooseFiles(() => Task.FromCanceled<IReadOnlyList<IPickedFile>>(cancelled));
        screen.Problem.Should().Be(previous);

        await screen.ChooseFiles(() => Task.FromResult<IReadOnlyList<IPickedFile>>([]));
        screen.Problem.Should().Be(previous);
        screen.Working.Should().BeNull();
        Server.Sent("POST", filePath).Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_keeps_an_oversized_file_named_and_uploads_the_later_valid_file()
    {
        var screen = Make<ProjectViewModel>();
        var tooBig = new SampleFile("oversized.bin", HostedMaxBytes + 1, [1]);
        var valid = new SampleFile("later.txt", 4, [1, 2, 3, 4]);
        Answer(maxFileBytes: HostedMaxBytes);
        await screen.Open(projectId);
        Server.Answer("POST", filePath, Note);
        Server.Answer<IReadOnlyList<ProjectFileResponse>>("GET", filePath, [Note]);

        await screen.UploadFiles([tooBig, valid]);

        screen.Problem.Should().Contain(tooBig.Name).And.Contain("10 MB");
        Server.Addresses.Where(address => address.StartsWith($"POST {filePath}", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("name=later.txt");
        Server.Addresses.Should().NotContain(address =>
            address.Contains("name=oversized.bin", StringComparison.Ordinal));
        screen.Files.Should().ContainSingle();
    }

    [Fact]
    public async Task A_batch_keeps_an_unreadable_file_named_and_uploads_the_later_valid_file()
    {
        const string RawFailure = "raw-batch-reader-failure";
        var log = new RecordingLogger<ProjectViewModel>();
        var screen = new ProjectViewModel(Server.CreateApi(), Session, Navigator, log);
        var unreadable = new SampleFile("private.txt", 4, [1, 2, 3, 4], new IOException(RawFailure));
        var valid = new SampleFile("later.txt", 4, [1, 2, 3, 4]);
        Answer(maxFileBytes: HostedMaxBytes);
        await screen.Open(projectId);
        Server.Answer("POST", filePath, Note);
        Server.Answer<IReadOnlyList<ProjectFileResponse>>("GET", filePath, [Note]);

        await screen.UploadFiles([unreadable, valid]);

        screen.Problem.Should().Contain(unreadable.Name).And.NotContain(RawFailure);
        Server.Addresses.Where(address => address.StartsWith($"POST {filePath}", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("name=later.txt");
        Server.Addresses.Should().NotContain(address => address.Contains("name=private.txt", StringComparison.Ordinal));
        log.Entries.Should().ContainSingle();
        log.Entries.Should().OnlyContain(entry =>
            entry.Exception == null && !entry.Message.Contains(RawFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_successful_upload_with_a_failed_refresh_is_not_reported_as_refused()
    {
        var screen = Make<ProjectViewModel>();
        var file = new SampleFile("later.txt", 4, [1, 2, 3, 4]);
        Answer();
        await screen.Open(projectId);
        Server.Answer("POST", filePath, Note);
        Server.Answer("GET", projectPath, HttpStatusCode.ServiceUnavailable);

        await screen.UploadFiles([file]);

        Server.Sent("POST", filePath).Should().ContainSingle();
        screen.Problem.Should().Contain("Uploaded later.txt.").And.Contain("Reopen");
        screen.Problem.Should().NotStartWith("later.txt:");
        screen.State.Should().Be(ProjectScreenState.Ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Downloading_delivers_the_authenticated_file_and_reports_save_failures(bool accessDenied)
    {
        var delivery = new RecordingDelivery();
        var log = new RecordingLogger<ProjectViewModel>();
        var screen = new ProjectViewModel(Server.CreateApi(), Session, Navigator, log, delivery);
        Answer(files: [Note]);
        await screen.Open(projectId);

        screen.Delivers.Should().BeTrue();
        await screen.Download(Note);

        delivery.Saved.Should().ContainSingle();
        delivery.Saved[0].Id.Should().Be(fileId);
        delivery.Saved[0].Name.Should().Be("note.txt");
        delivery.Saved[0].ContentType.Should().Be("text/plain");
        delivery.Saved[0].Size.Should().Be(4);
        delivery.Saved[0].Href.Should().Be(screen.DownloadAddress(fileId));
        screen.Problem.Should().BeNull();

        const string RawFailure = "private-save-failure";
        delivery.NextFailure = accessDenied
            ? new UnauthorizedAccessException(RawFailure)
            : new IOException(RawFailure);
        await screen.Download(Note);

        screen.Problem.Should().Be("Could not save note.txt.");
        log.Entries.Should().ContainSingle(entry =>
            entry.Exception == null && !entry.Message.Contains(RawFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_a_file_sends_the_delete()
    {
        var screen = Make<ProjectViewModel>();
        Answer(files: [Note]);
        await screen.Open(projectId);
        Server.Answer("DELETE", $"{filePath}/{fileId}", HttpStatusCode.NoContent);
        Server.Answer("GET", filePath, Array.Empty<ProjectFileResponse>());

        await screen.RemoveFile(Note);

        Server.Sent("DELETE", $"{filePath}/{fileId}").Should().ContainSingle();
        screen.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Starting_a_conversation_skips_paused_bots_and_sends_only_the_project()
    {
        var screen = Make<ProjectViewModel>();
        Answer(bots: [Marshal, Paused]);
        await screen.Open(projectId);
        Server.Answer("POST", $"{space}/bots/{botId}/threads", Thread(threadId, botId, null, projectId));

        screen.ActiveBots.Select(bot => bot.Id).Should().Equal(botId);
        ProjectViewModel.NoBot.Should().Be("This space has no active bots yet.");
        await screen.Start(botId);

        Navigator.Current.Should().Be(Addresses.Conversation(threadId));
        var body = Server.Sent("POST", $"{space}/bots/{botId}/threads").Single();
        body.Should().Contain(projectId.ToString());
        body.Should().NotContain("audience");
        body.Should().NotContain("model");
    }

    [Fact]
    public async Task Moving_a_conversation_sends_only_its_project_and_a_refusal_stays()
    {
        var outside = Thread(outsideId, botId, "Side notes", null);
        var screen = Make<ProjectViewModel>();
        Answer(bots: [Marshal], own: [outside]);
        await screen.Open(projectId);
        Server.Refuse("PUT", $"{space}/threads/{outsideId}/project", HttpStatusCode.Forbidden, "Only the conversation's owner can move it.");

        await screen.MoveIn(outsideId);

        screen.Problem.Should().Be("Only the conversation's owner can move it.");
        screen.State.Should().Be(ProjectScreenState.Ready);
        Navigator.Current.Should().Be(Addresses.Home);
        var refused = Server.Sent("PUT", $"{space}/threads/{outsideId}/project").Single();
        refused.Should().Contain(projectId.ToString());
        refused.Should().NotContain("audience");
        refused.Should().NotContain("\"model\"");

        var moved = outside with { ProjectId = projectId };
        Server.Answer("PUT", $"{space}/threads/{outsideId}/project", moved);
        Server.Answer("GET", threadPath, new[] { moved });
        Server.Answer("GET", $"{space}/threads", new[] { moved });
        await screen.MoveIn(outsideId);
        var before = Server.Requests.Count;
        await screen.MoveOut(outsideId);
        var added = Server.Requests.Skip(before).ToList();

        added.Where(request => request.Method != "GET").Should().ContainSingle()
            .Which.Path.Should().Be($"{space}/threads/{outsideId}/project");
        added.Single(request => request.Method == "PUT").Body.Should().Contain("\"projectId\":null");
        screen.Threads.Should().ContainSingle().Which.Id.Should().Be(outsideId);
        screen.Movable.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_an_owned_conversation_currently_in_the_project_can_move_out()
    {
        var ownedInside = Thread(threadId, botId, "Weekly notes", projectId);
        var ownedOutside = Thread(outsideId, botId, "Side notes", null);
        var otherInside = Thread(Guid.Parse("0196f1a0-0000-7000-8000-000000000018"), botId, "Other notes", projectId);
        var screen = Make<ProjectViewModel>();
        Answer(threads: [ownedInside, otherInside], bots: [Marshal], own: [ownedInside, ownedOutside]);

        await screen.Open(projectId);

        screen.CanMoveOut(ownedInside.Id).Should().BeTrue();
        screen.CanMoveOut(ownedOutside.Id).Should().BeFalse();
        screen.CanMoveOut(otherInside.Id).Should().BeFalse();
        screen.CanMoveOut(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public async Task Moving_another_persons_conversation_out_is_refused_without_a_request()
    {
        var otherInside = Thread(threadId, botId, "Other notes", projectId);
        var screen = Make<ProjectViewModel>();
        Answer(threads: [otherInside], bots: [Marshal]);
        await screen.Open(projectId);

        await screen.MoveOut(otherInside.Id);

        screen.Problem.Should().Contain("your own conversations");
        Server.Sent("PUT", $"{space}/threads/{otherInside.Id}/project").Should().BeEmpty();
    }

    [Fact]
    public async Task A_missing_project_says_so_and_a_cancelled_load_is_dropped()
    {
        SignedIn();
        var missing = Make<ProjectViewModel>();
        await missing.Open(projectId);

        missing.State.Should().Be(ProjectScreenState.Missing);
        ProjectViewModel.MissingTitle.Should().Be("Project not found");
        ProjectViewModel.MissingCause.Should().Be("It was removed, or it is in another space.");

        Server.Answer("GET", projectPath, Weekly);
        var held = Server.Hold("GET", projectPath);
        var screen = Make<ProjectViewModel>();
        var opening = screen.Open(projectId);
        screen.State.Should().Be(ProjectScreenState.Loading);
        ProjectViewModel.Loading.Should().Be("Loading the project.");
        screen.Cancel();
        held.SetResult();
        await opening;

        screen.Project.Should().BeNull();
        Answer();
        await screen.Open(projectId);
        screen.State.Should().Be(ProjectScreenState.Ready);
        screen.Name.Should().Be("Weekly");
    }

    [Fact]
    public async Task A_server_that_refuses_the_project_says_so()
    {
        SignedIn();
        Server.Refuse("GET", projectPath, HttpStatusCode.Forbidden, "Only a member of the space can read this project.");
        var screen = Make<ProjectViewModel>();

        await screen.Open(projectId);

        screen.State.Should().Be(ProjectScreenState.Failed);
        ProjectViewModel.LoadFailedTitle.Should().Be("Drover could not load the project");
        screen.LoadProblem.Should().Be("Only a member of the space can read this project.");
    }

    [Fact]
    public async Task An_unreachable_server_when_opening_reports_failure_without_throwing()
    {
        Server.Unreachable = true;
        var screen = Make<ProjectViewModel>();

        await screen.Open(projectId);

        screen.State.Should().Be(ProjectScreenState.Failed);
        screen.LoadProblem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Errors_keep_server_problem_titles_and_raw_exceptions_out_of_logs()
    {
        SignedIn();
        const string SecretTitle = "Secret-server-title-only-for-ui";
        Server.Refuse("GET", projectPath, HttpStatusCode.Forbidden, SecretTitle);
        var log = new RecordingLogger<ProjectViewModel>();
        var screen = new ProjectViewModel(Server.CreateApi(), Session, Navigator, log);

        await screen.Open(projectId);

        screen.LoadProblem.Should().Be(SecretTitle);
        log.Entries.Should().NotBeEmpty();
        log.Entries.Should().OnlyContain(entry => entry.Exception == null && !entry.Message.Contains(SecretTitle, StringComparison.Ordinal));
    }

    #endregion

    #region Helpers

    private static ProjectSummary Weekly { get; } = new(projectId, "Weekly", "Ship the note.", when, when);

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Stay brief.", connectionId, "sample", "active");

    private static BotSummary Paused { get; } = new(pausedBotId, "Paused", "paused", "Stay brief.", connectionId, "sample", "paused");

    private static ProjectFileResponse Note { get; } = new(new(fileId, "note.txt", "text/plain", FileKinds.Text, 4), projectId, when);

    private static ThreadSummary Thread(Guid id, Guid bot, string? title, Guid? project) =>
        new(id, bot, title, false, when, null, false, null, project);

    private void Answer(
        string role = "owner",
        IReadOnlyList<ThreadSummary>? threads = null,
        IReadOnlyList<ProjectFileResponse>? files = null,
        IReadOnlyList<BotSummary>? bots = null,
        IReadOnlyList<ThreadSummary>? own = null,
        long maxFileBytes = 25L * 1024 * 1024)
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [Person.Spaces[0] with { Role = role }] });
        Server.Answer("GET", projectPath, Weekly);
        Server.Answer("GET", threadPath, threads ?? []);
        Server.Answer("GET", filePath, files ?? []);
        Server.Answer("GET", $"{space}/files/rules", new FileRules(maxFileBytes));
        Server.Answer("GET", $"{space}/bots", bots ?? []);
        Server.Answer("GET", $"{space}/threads", own ?? []);
    }

    #endregion

    #region Sample Files

    private sealed class SampleFile(string name, long size, byte[] bytes, Exception? failure = null) : IPickedFile
    {
        #region Properties

        public string Name => name;

        public long Size => size;

        public long RequestedCap { get; private set; }

        #endregion

        #region Public Methods

        public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedCap = maxBytes;
            return failure is null
                ? Task.FromResult<Stream>(new MemoryStream(bytes))
                : Task.FromException<Stream>(failure);
        }

        #endregion
    }

    private sealed class RecordingDelivery : IFileDelivery
    {
        #region Properties

        public List<SharedFile> Saved { get; } = [];

        public Exception? NextFailure { get; set; }

        #endregion

        #region Public Methods

        public Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default)
        {
            if (NextFailure is { } failure)
            {
                NextFailure = null;
                return Task.FromException<bool>(failure);
            }

            Saved.Add(file);
            return Task.FromResult(true);
        }

        public Task Share(SharedFile file, CancellationToken cancellationToken = default) => Task.CompletedTask;

        #endregion
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        #region Properties

        public List<(Exception? Exception, string Message)> Entries { get; } = [];

        #endregion

        #region Public Methods

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((exception, formatter(state, exception)));

        #endregion
    }

    #endregion
}
