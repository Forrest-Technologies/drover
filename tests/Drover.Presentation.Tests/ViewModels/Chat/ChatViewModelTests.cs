namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The parts of a conversation, each on its own: the crew's rows, the side chats, the message list, the composer,
/// what a reply used, and the menu about a bot. The cases are the web client's component tests. Every name and
/// value is made up.
/// </summary>
public sealed class ChatViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid first = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a");

    private static readonly Guid second = Guid.Parse("0196f1a0-0000-7000-8000-00000000000b");

    private static readonly BotSummary bot = new(
        Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1"), "Sample bot", "sample-bot", string.Empty, Guid.Empty, "sample-model", "active");

    private readonly FakeServer server = new();

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    #endregion

    #region Crew And Side Chats

    [Fact]
    public void Each_bot_is_a_row_to_its_main_conversation_under_the_bot_that_manages_it()
    {
        var lead = new BotSummary(first, "Sample lead", "sample-lead", string.Empty, Guid.Empty, "sample-model", "active");
        var helper = new BotSummary(second, "Sample helper", "sample-helper", string.Empty, Guid.Empty, "sample-model", "active", first);
        var working = ImmutableHashSet.Create(second);
        var quiet = ImmutableDictionary<Guid, Attention>.Empty;

        var rows = ChatRows.CrewRows(CrewTree.From([helper, lead]));

        rows.Select(row => Addresses.Talk(row.Bot.Id)).Should().Equal($"/talk/{first}", $"/talk/{second}");
        rows.Select(row => row.Depth).Should().Equal([0, 1], "the helper stands under the bot that manages it");
        ChatRows.CrewRowName(lead, working, quiet).Should().Be("Sample lead");
        ChatRows.CrewRowName(helper, working, quiet).Should().Be("Sample helper, thinking");
    }

    [Fact]
    public void A_bot_whose_parent_is_not_listed_or_points_back_at_it_is_still_in_the_crew()
    {
        var orphan = new BotSummary(first, "Sample orphan", "sample-orphan", string.Empty, Guid.Empty, "sample-model", "paused", Guid.Parse("0196f1a0-0000-7000-8000-00000000000c"));
        var looped = new BotSummary(second, "Sample loop", "sample-loop", string.Empty, Guid.Empty, "sample-model", "active", second);

        var crew = CrewTree.From([orphan, looped]);

        ChatRows.CrewRows(crew).Select(row => row.Bot.Id).Should().Equal(first, second);
        CrewTree.Lead([orphan, looped])!.Id.Should().Be(second, "the lead is the first bot at the top that can answer");
        ChatRows.IsPaused(orphan).Should().BeTrue();
        ChatRows.CrewRowName(orphan, ImmutableHashSet<Guid>.Empty, new Dictionary<Guid, Attention> { [first] = Attention.New })
            .Should().Be("Sample orphan, paused, new");
    }

    [Fact]
    public void A_quiet_bot_shows_no_attention_and_a_waiting_one_says_so_in_words()
    {
        AttentionWords.Word(Attention.None).Should().BeNull("a quiet crew stays quiet");
        AttentionWords.Word(Attention.WaitingForYou).Should().Be("Waiting for you");
        AttentionWords.Kind(Attention.WaitingForYou).Should().Be(BadgeKind.Warn);
    }

    #endregion

    #region Message List

    [Fact]
    public void The_conversation_has_a_name_and_an_empty_one_says_what_to_do()
    {
        var list = new MessageListViewModel { BotName = "Sample bot", BotHue = 2 };
        list.Update();

        list.Name.Should().Be("Conversation with Sample bot");
        list.IsEmpty.Should().BeTrue();
        list.StartHint.Should().Be("No messages yet. Write to Sample bot below.");

        list.Thinking = true;
        list.Update();
        list.IsEmpty.Should().BeFalse("the bot is on its way to an answer");
    }

    [Fact]
    public void A_streaming_reply_is_read_out_one_finished_sentence_at_a_time()
    {
        var list = new MessageListViewModel { BotName = "Sample bot", Messages = [new("2", false, "The build", Streaming: true)] };
        list.Update();
        list.Spoken.Should().BeNull();

        list.Messages = [new("2", false, "The build passed. Next", Streaming: true)];
        list.Update();
        list.Spoken.Should().Be("The build passed.");

        list.Messages = [new("2", false, "The build passed. Next step", Streaming: false)];
        list.Update();
        list.Spoken.Should().Be("Next step");
    }

    [Fact]
    public void A_message_a_schedule_sent_says_so_in_words_and_is_named_for_it_not_for_the_person()
    {
        var scheduled = new ChatMessage("1", true, "Brief me on what changed overnight.") { SentBy = "Sent by the schedule 'Every weekday at 09:30'" };
        var typed = new ChatMessage("2", true, "Thanks");

        ChatRows.EntryName(scheduled, "Sample bot", null).Should().Be("Sent by the schedule 'Every weekday at 09:30'");
        ChatRows.EntryName(typed, "Sample bot", null).Should().Be("You said");
        ChatRows.EntryName(typed, "Sample bot", "Sample lead").Should().Be("Sample lead asked", "on a bot's side of handed work the bot above wrote the task");
    }

    [Fact]
    public void A_persons_message_a_bots_words_and_a_bots_calls_each_have_their_shape_and_their_name()
    {
        var said = new ChatMessage("1", false, "Sample **answer**");
        var did = new ChatMessage("2", false, string.Empty);

        ChatRows.Shape(new ChatMessage("0", true, "Sample question"), compact: false).Should().Be(ChatEntryShape.Person);
        ChatRows.Shape(said, compact: false).Should().Be(ChatEntryShape.Bot);
        ChatRows.Shape(said, compact: true).Should().Be(ChatEntryShape.Compact, "inside handed work the card has the bot's head");
        ChatRows.Shape(did, compact: false).Should().Be(ChatEntryShape.Tools);
        ChatRows.EntryName(said, "Sample bot", null).Should().Be("Sample bot said");
        ChatRows.EntryName(did, "Sample bot", null).Should().Be("Sample bot's activity");
        ChatRows.Pending("Sample bot").Should().Be("Sent. Sample bot reads it at its next step.");
    }

    [Fact]
    public void A_failed_run_says_what_failed_and_offers_one_way_forward()
    {
        var retried = 0;
        var list = new MessageListViewModel
        {
            BotName = "Sample bot",
            Failure = new(Guid.CreateVersion7(), FailureCauses.ModelUnreachable, "connection refused"),
            OnRetry = () =>
            {
                retried++;
                return Task.CompletedTask;
            },
        };
        list.Update();

        AttentionWords.Failure(list.FailureCause!.Value, list.BotName).Should().StartWith("Sample bot could not reach its model.");
        list.Spoken.Should().StartWith("Sample bot could not reach its model.", "a failure is read out once, when it appears");
        list.Retry();
        retried.Should().Be(1);
    }

    [Fact]
    public void While_a_retry_is_asked_for_a_second_press_does_nothing()
    {
        var presses = 0;
        var list = new MessageListViewModel
        {
            BotName = "Sample bot",
            Retrying = true,
            OnRetry = () =>
            {
                presses++;
                return Task.CompletedTask;
            },
        };

        list.Retry();

        presses.Should().Be(0);
    }

    [Fact]
    public void A_request_for_the_person_at_a_screen_has_its_own_card_and_a_bot_beneath_answers_at_its_own_screen()
    {
        var takeover = new Takeover(Guid.CreateVersion7(), "Sample reason", TakeoverPhase.Asked);
        var call = new ToolCard(takeover.ToolCallId, ToolPhase.WaitingForYou, "Sample label", "Sample request", "Sample summary", "{}", false, null, null, [])
        {
            Takeover = takeover,
        };
        var here = new ScreenTakeovers(takeover.ToolCallId, ScreenAccess.Usable, "/bots/sample", null, null);
        var work = new DelegationItem(Guid.CreateVersion7(), "Sample bot", first, "Sample helper", "Sample task", DelegationPhase.Working, null, default, null, 0);

        ChatRows.Shape(call, here).Should().Be(ToolCardShape.Takeover);
        ChatRows.Shape(call, null).Should().Be(ToolCardShape.Plain, "without the screen around it the request is a plain tool card");
        ChatRows.Shape(call with { Takeover = null, Delegation = work }, here).Should().Be(ToolCardShape.Delegation);

        var beneath = ChatRows.ForBotBeneath(here, work)!;
        beneath.Access.Should().Be(ScreenAccess.Elsewhere);
        beneath.HeldCallId.Should().BeNull();
        beneath.SetupHref.Should().Be(Addresses.BotSandbox(first));
        ChatRows.ForBotBeneath(here with { Access = ScreenAccess.NotPermitted }, work)!.Access.Should().Be(ScreenAccess.NotPermitted);
        ChatRows.ForBotBeneath(null, work).Should().BeNull();
    }

    #endregion

    #region Composer

    [Fact]
    public void The_composer_is_one_named_field_and_a_send_button_that_waits_for_text()
    {
        var composer = new ComposerViewModel { BotName = "Sample bot" };

        composer.FieldName.Should().Be("Message to Sample bot");
        composer.Placeholder.Should().Be("Write to Sample bot");
        (composer.ShowsSend, composer.ShowsStop).Should().Be((true, false));
        composer.CanSend.Should().BeFalse();

        composer.Text = "Hello";

        composer.CanSend.Should().BeTrue();
    }

    [Fact]
    public async Task Sending_hands_over_the_trimmed_text_and_empties_the_field()
    {
        List<string> sent = [];
        var composer = Composer(onSend: sent.Add);

        composer.Text = "  Hello there \n";
        await composer.Send();

        sent.Should().Equal("Hello there");
        composer.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task A_send_that_fails_puts_the_words_back_in_the_field()
    {
        var sending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var composer = new ComposerViewModel { OnSend = _ => sending.Task };
        var focused = 0;
        composer.FocusRequested += () => focused++;

        composer.Text = "  Hello there \n";
        var send = composer.Send();
        composer.Text.Should().BeEmpty("the field clears while the request is on its way");

        sending.SetResult(false);
        await send;

        composer.Text.Should().Be("Hello there");
        focused.Should().Be(1, "the restore puts the cursor back once");
    }

    [Fact]
    public async Task Words_typed_while_a_failed_send_was_on_its_way_follow_the_restored_message()
    {
        var sending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var composer = new ComposerViewModel { OnSend = _ => sending.Task };

        composer.Text = "Hello there";
        var send = composer.Send();
        composer.Text = "And a question";

        sending.SetResult(false);
        await send;

        composer.Text.Should().Be("Hello there\n\nAnd a question", "what the person wrote meanwhile is kept after the failed words");
    }

    [Fact]
    public async Task A_send_that_throws_restores_the_words_and_lets_the_failure_go_on()
    {
        var composer = new ComposerViewModel { OnSend = _ => throw new InvalidOperationException("Unexpected failure.") };
        composer.Text = "Hello there";

        var send = async () => await composer.Send();
        await send.Should().ThrowAsync<InvalidOperationException>();

        composer.Text.Should().Be("Hello there");
    }

    [Fact]
    public async Task While_a_run_is_active_stop_takes_the_place_of_send()
    {
        var stopped = 0;
        var composer = Composer(running: true, onCancel: () => stopped++);

        (composer.ShowsStop, composer.ShowsSend).Should().Be((true, false));
        composer.IsWorking.Should().BeTrue();
        await composer.Cancel();

        stopped.Should().Be(1);
    }

    [Fact]
    public async Task A_message_written_while_the_bot_works_can_be_sent_and_stop_stays()
    {
        List<string> sent = [];
        var composer = Composer(running: true, onSend: sent.Add);

        composer.Text = "Use the shorter name";
        (composer.ShowsStop, composer.ShowsSend, composer.HasTwoActions).Should().Be((true, true, true));
        await composer.Send();

        sent.Should().Equal("Use the shorter name");
        (composer.ShowsStop, composer.ShowsSend).Should().Be((true, false));
    }

    [Fact]
    public void While_the_run_waits_for_a_person_the_line_along_the_top_rests()
    {
        var composer = Composer(running: true);

        composer.Waiting = true;

        composer.IsWorking.Should().BeFalse();
        composer.ShowsStop.Should().BeTrue();
    }

    [Fact]
    public void Writing_a_draft_puts_the_cursor_in_the_field_and_the_options_row_takes_the_focus_when_it_opens()
    {
        var composer = Composer();
        var (field, options) = (0, 0);
        composer.FocusRequested += () => field++;
        composer.OptionsFocusRequested += () => options++;

        composer.Write("Sample words");
        composer.ToggleOptions();
        composer.ToggleOptions();

        composer.Text.Should().Be("Sample words");
        (field, options).Should().Be((1, 1));
        composer.OptionsOpen.Should().BeFalse();
    }

    [Fact]
    public void A_file_still_uploading_or_one_that_cannot_go_holds_the_message()
    {
        var composer = Composer();
        composer.Text = "Here";

        composer.Attachments = [new(Guid.CreateVersion7(), "notes.txt", 3, UploadPhase.Uploading, 0)];
        composer.CanSend.Should().BeFalse();
        ChatRows.Announced(composer.Attachments).Should().Be("Uploading 1 file.");

        composer.Attachments = [new(Guid.CreateVersion7(), "notes.txt", 3, UploadPhase.Ready, 100)];
        composer.CanSend.Should().BeTrue();
        ChatRows.Announced(composer.Attachments).Should().Be("1 file ready to send.");

        composer.Text = string.Empty;
        composer.HasSomething.Should().BeTrue("a message may be only its files");
    }

    [Fact]
    public async Task The_model_choice_is_offered_only_when_the_bot_may_answer_with_more_than_one()
    {
        List<string?> chosen = [];
        var composer = Composer();
        composer.OnModelChosen = model =>
        {
            chosen.Add(model);
            return Task.CompletedTask;
        };

        composer.ShowModels(new ConversationModelChoice("sample-model", "Sample model", null, [new("sample-model", "Sample model")]));
        composer.HasModelChoice.Should().BeFalse("with only the bot's own model there is nothing to choose");

        composer.ShowModels(new ConversationModelChoice(
            "sample-model",
            "Sample model",
            null,
            [new("sample-model", "Sample model"), new("sample-stronger", "Sample stronger")]));

        ComposerViewModel.ModelLabel.Should().Be("Model for this conversation");
        composer.HasModelChoice.Should().BeTrue();
        composer.ModelOptions.Should().Equal(
            new ModelOption(ComposerViewModel.BotsOwnModel, "Sample model (the bot's own)"),
            new ModelOption("sample-stronger", "Sample stronger"));
        composer.ChosenModel.Should().Be(ComposerViewModel.BotsOwnModel);

        await composer.ChooseModel("sample-stronger");
        await composer.ChooseModel("sample-stronger");
        await composer.ChooseModel(ComposerViewModel.BotsOwnModel);

        chosen.Should().Equal(["sample-stronger", null], "choosing what is already chosen asks nothing, and the bot's own is no choice at all");
        composer.ShowModels(null);
        composer.HasModelChoice.Should().BeFalse("nothing is offered while the server has not said what may be chosen");
    }

    [Fact]
    public async Task More_than_a_hundred_files_takes_the_first_hundred_and_says_so()
    {
        List<string> handed = [];
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer();
        composer.OnFiles = files =>
        {
            handed = [.. files.Select(file => file.Name)];
            return Task.CompletedTask;
        };
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        await composer.Chosen(SampleFiles(100));

        handed.Should().HaveCount(100);
        told.Should().BeEmpty("exactly a hundred is all that was asked for");

        await composer.Chosen(SampleFiles(101));

        handed.Should().HaveCount(100).And.StartWith("file 1.txt").And.EndWith("file 100.txt");
        told.Should().Equal([("Only the first 100 files were taken.", ToastKind.Warning)]);
    }

    [Fact]
    public async Task Files_past_the_hundredth_are_counted_and_never_read()
    {
        List<int> read = [];
        var items = Enumerable.Range(1, 150).ToList();

        var handed = await HandedFiles.ReadFirst(items, number =>
        {
            read.Add(number);
            return number > ComposerViewModel.MaxFilesAtOnce
                ? throw new IOException("A file past the hundredth was read.")
                : Task.FromResult<IPickedFile>(new SamplePickedFile($"file {number}.txt", "one"));
        });

        read.Should().HaveCount(100, "only the files that can be taken are asked for their details");
        handed.Files.Should().HaveCount(100);
        handed.Beyond.Should().Be(50);
    }

    [Fact]
    public async Task Files_handed_over_past_the_most_one_go_takes_are_said_to_be_left_out()
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer();
        IReadOnlyList<IPickedFile> handed = [];
        composer.OnFiles = files =>
        {
            handed = files;
            return Task.CompletedTask;
        };
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        await composer.Hand(new(SampleFiles(100), Beyond: 1));

        handed.Should().HaveCount(100);
        told.Should().Equal([("Only the first 100 files were taken.", ToastKind.Warning)]);
    }

    [Fact]
    public async Task Folders_alone_are_said_to_be_left_out_and_nothing_is_attached()
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer();
        var attached = false;
        composer.OnFiles = _ =>
        {
            attached = true;
            return Task.CompletedTask;
        };
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        await composer.Hand(new([], Folders: 2));

        attached.Should().BeFalse();
        told.Should().Equal([("Folders cannot be attached. Attach the files inside.", ToastKind.Warning)]);
    }

    [Fact]
    public void A_clipboard_that_cannot_be_read_says_the_paste_failed()
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer(takesFiles: true);
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        var taken = composer.Pasting(
            () => throw new InvalidOperationException("The clipboard is held by another program."),
            NullLogger.Instance);

        taken.Should().BeFalse("nothing was read, so the field keeps the paste");
        told.Should().Equal([("What was pasted could not be attached.", ToastKind.Error)]);
    }

    [Theory]
    [InlineData(ClipboardHolds.Text)]
    [InlineData(ClipboardHolds.Nothing)]
    public void Text_or_an_empty_clipboard_is_left_to_the_field_and_says_nothing(ClipboardHolds holds)
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer(takesFiles: true);
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        composer.Pasting(() => holds, NullLogger.Instance).Should().BeFalse();

        told.Should().BeEmpty();
    }

    [Fact]
    public void A_paste_of_files_is_taken_from_the_field()
    {
        var composer = Composer(takesFiles: true);

        composer.Pasting(() => ClipboardHolds.Files, NullLogger.Instance).Should().BeTrue();
    }

    [Fact]
    public void A_picture_the_device_did_not_hand_over_is_said_in_one_sentence()
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer(takesFiles: true);
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        composer.Pasting(() => ClipboardHolds.PictureNotHandedOver, NullLogger.Instance).Should().BeFalse();

        told.Should().Equal(
            [("This device did not hand over the picture. Save it as a file and attach that.", ToastKind.Warning)]);
    }

    [Fact]
    public void A_conversation_that_takes_no_files_leaves_a_paste_to_the_field_without_reading_the_clipboard()
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer(takesFiles: false);
        composer.OnNotice = (words, kind) => told.Add((words, kind));
        var looked = false;

        var taken = composer.Pasting(() =>
        {
            looked = true;
            return ClipboardHolds.Files;
        }, NullLogger.Instance);

        taken.Should().BeFalse();
        looked.Should().BeFalse();
        told.Should().BeEmpty();
    }

    [Fact]
    public async Task Files_that_cannot_be_read_say_whether_they_were_pasted_or_dropped()
    {
        List<(string Words, ToastKind Kind)> told = [];
        var composer = Composer(takesFiles: true);
        composer.OnNotice = (words, kind) => told.Add((words, kind));

        await composer.TakePasted(() => throw new IOException("The picture could not be read."), NullLogger.Instance);
        await composer.TakeDropped(() => throw new IOException("The file could not be read."), NullLogger.Instance);

        told.Should().Equal(
        [
            ("What was pasted could not be attached.", ToastKind.Error),
            ("What was dropped could not be attached.", ToastKind.Error),
        ]);
    }

    [Fact]
    public async Task Files_read_while_the_conversation_stops_taking_them_are_not_handed_on()
    {
        var composer = Composer(takesFiles: true);
        var attached = false;
        composer.OnFiles = _ =>
        {
            attached = true;
            return Task.CompletedTask;
        };
        var reading = new TaskCompletionSource<HandedFiles>();

        var dropped = composer.TakeDropped(() => reading.Task, NullLogger.Instance);
        composer.TakesFiles = false;
        reading.SetResult(new(SampleFiles(1)));
        await dropped;

        attached.Should().BeFalse("the conversation stopped taking files while they were read");
    }

    [Fact]
    public async Task Dropping_files_clears_the_drop_line()
    {
        var composer = Composer(takesFiles: true);
        var attached = 0;
        composer.OnFiles = files =>
        {
            attached += files.Count;
            return Task.CompletedTask;
        };

        composer.ShowDropping(true);
        composer.Dropping.Should().BeTrue();

        await composer.TakeDropped(() => Task.FromResult(new HandedFiles(SampleFiles(1))), NullLogger.Instance);

        composer.Dropping.Should().BeFalse();
        attached.Should().Be(1);

        composer.ShowDropping(true);
        composer.TakesFiles = false;
        composer.Dropping.Should().BeFalse("a conversation that stops taking files clears the drop line");
    }

    [Fact]
    public void Cancelling_a_drag_clears_the_drop_line_without_attaching_anything()
    {
        var composer = Composer(takesFiles: true);
        var attached = 0;
        List<(string Words, ToastKind Kind)> told = [];
        List<string?> changed = [];
        composer.OnFiles = files =>
        {
            attached += files.Count;
            return Task.CompletedTask;
        };
        composer.OnNotice = (words, kind) => told.Add((words, kind));
        composer.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        composer.ShowDropping(true);
        composer.Dropping.Should().BeTrue();
        changed.Clear();

        composer.CancelDropping();

        composer.Dropping.Should().BeFalse();
        changed.Should().Contain(nameof(ComposerViewModel.Dropping));
        attached.Should().Be(0);
        told.Should().BeEmpty();
    }

    #endregion

    #region What A Reply Used

    [Fact]
    public async Task The_line_is_read_only_when_a_person_opens_it_and_says_the_runs_stored_figures()
    {
        var runId = Guid.CreateVersion7();
        List<Guid> asked = [];
        var line = new ReplyUsageViewModel(
            runId,
            id =>
            {
                asked.Add(id);
                return Task.FromResult(new RunUsageDetail(id, "USD", new(0.02m, 1_200, 340), new(0.10m, 4_000, 900)));
            },
            NullLogger.Instance);

        ReplyUsageViewModel.Label.Should().Be("Usage for this reply");
        line.Open.Should().BeFalse();
        asked.Should().BeEmpty("a closed line asks the server for nothing");

        await line.Toggle();

        line.Lines.Should().Equal(
            "This reply: USD 0.02, 1,200 tokens in, 340 out.",
            "With the bots it asked: USD 0.10, 4,000 tokens in, 900 out.");
        line.Open.Should().BeTrue();

        await line.Toggle();
        line.Lines.Should().BeEmpty("a closed line shows nothing");
        await line.Toggle();
        asked.Should().Equal([runId], "what a finished run used does not change, so it is read once");
    }

    [Fact]
    public async Task A_failed_read_says_what_failed_and_can_be_tried_again()
    {
        var reachable = false;
        var line = new ReplyUsageViewModel(
            Guid.CreateVersion7(),
            id => reachable
                ? Task.FromResult(new RunUsageDetail(id, "USD", new(0.01m, 10, 1), null))
                : Task.FromException<RunUsageDetail>(new HttpRequestException("The server cannot be reached.")),
            NullLogger.Instance);

        await line.Toggle();

        line.Problem.Should().NotBeNullOrEmpty();
        line.Lines.Should().BeEmpty();
        line.IsAdding.Should().BeFalse();

        reachable = true;
        await line.Read();

        line.Problem.Should().BeNull();
        line.Lines.Should().Equal("This reply: USD 0.01, 10 tokens in, 1 out.");
    }

    [Fact]
    public void The_line_names_the_model_that_wrote_the_reply_when_the_server_kept_it()
    {
        var named = new ReplyUsageViewModel(Guid.CreateVersion7(), id => Task.FromException<RunUsageDetail>(new InvalidOperationException()), NullLogger.Instance, "sample-stronger");
        var older = new ReplyUsageViewModel(Guid.CreateVersion7(), id => Task.FromException<RunUsageDetail>(new InvalidOperationException()), NullLogger.Instance);

        named.WrittenBy.Should().Be("Model: sample-stronger");
        older.WrittenBy.Should().BeNull("a reply written before the model was kept names none");
    }

    [Fact]
    public void Only_a_reply_that_ended_shows_the_control()
    {
        var aloud = new ChatMessage("a", false, "Thinking aloud.") { RunId = Guid.CreateVersion7() };
        var plan = new ChatMessage("b", false, "Here is the plan.") { RunId = Guid.CreateVersion7(), EndsReply = true };

        ChatRows.OffersUsage(aloud, canRead: true).Should().BeFalse();
        ChatRows.OffersUsage(plan, canRead: true).Should().BeTrue();
        ChatRows.OffersUsage(plan, canRead: false).Should().BeFalse("without a way to read it, replies show no usage");
    }

    #endregion

    #region The Menu About A Bot

    [Fact]
    public async Task The_item_says_what_it_will_do_and_choosing_it_tells_the_server()
    {
        var path = $"/api/spaces/{Samples.Session().Spaces[0].Id}/bots/{bot.Id}/notify";
        server.Answer("GET", path, new BotNotifySetting(true));
        server.Answer("PUT", path, new BotNotifySetting(false));
        var (menu, toasts) = await Menu();

        await menu.Show(bot);

        menu.NotifyLabel.Should().Be("Stop notifying me about Sample bot");
        BotNotifyMenuViewModel.Label.Should().Be("More");
        menu.AccessibleLabel.Should().Be("More about Sample bot");
        menu.HasMenu.Should().BeTrue();

        await menu.Flip();

        menu.NotifyLabel.Should().Be("Notify me about Sample bot");
        server.Requests.Should().Contain(request => request.Method == "PUT" && request.Path == path && request.Body.Contains("false"));
        toasts.Toasts.Single().Message.Should().Be("Sample bot will not notify you.");
    }

    [Fact]
    public async Task Nothing_shows_until_the_server_has_said_which_way_it_is_set()
    {
        var path = $"/api/spaces/{Samples.Session().Spaces[0].Id}/bots/{bot.Id}/notify";
        server.Answer("GET", path, HttpStatusCode.NotFound);
        var (menu, _) = await Menu();

        await menu.Show(bot);

        server.Requests.Should().Contain(request => request.Path == path);
        menu.HasMenu.Should().BeFalse();
        menu.NotifyLabel.Should().BeNull();

        menu.HasItems = true;
        menu.HasMenu.Should().BeTrue("the screen put its own items in the menu");
    }

    [Fact]
    public async Task A_choice_the_server_does_not_take_is_said_and_the_item_stays_as_it_was()
    {
        var path = $"/api/spaces/{Samples.Session().Spaces[0].Id}/bots/{bot.Id}/notify";
        server.Answer("GET", path, new BotNotifySetting(false));
        server.Answer("PUT", path, HttpStatusCode.InternalServerError);
        var (menu, toasts) = await Menu();
        await menu.Show(bot);

        await menu.Flip();

        menu.NotifyLabel.Should().Be("Notify me about Sample bot");
        toasts.Toasts.Single().Should().Match<ToastMessage>(toast => toast.Kind == ToastKind.Error && toast.Message.StartsWith("The choice was not saved. "));
    }

    #endregion

    #region Private Methods

    private static IReadOnlyList<IPickedFile> SampleFiles(int count) =>
        [.. Enumerable.Range(1, count).Select(number => new SamplePickedFile($"file {number}.txt", "one"))];

    private static ComposerViewModel Composer(
        bool running = false,
        Action<string>? onSend = null,
        Action? onCancel = null,
        bool takesFiles = false) => new()
        {
            BotName = "Sample bot",
            Running = running,
            TakesFiles = takesFiles,
            OnSend = text =>
            {
                onSend?.Invoke(text);
                return Task.FromResult(true);
            },
            OnCancel = () =>
            {
                onCancel?.Invoke();
                return Task.CompletedTask;
            },
        };

    private async Task<(BotNotifyMenuViewModel Menu, ToastService Toasts)> Menu()
    {
        server.Answer("GET", "/api/session", Samples.Session());
        var api = server.CreateApi();
        var session = new SessionState(api, new ShareInbox());
        await session.Load(TestContext.Current.CancellationToken);
        var toasts = new ToastService(TimeProvider.System);
        return (new(api, session, toasts, NullLogger<BotNotifyMenuViewModel>.Instance), toasts);
    }

    #endregion
}
