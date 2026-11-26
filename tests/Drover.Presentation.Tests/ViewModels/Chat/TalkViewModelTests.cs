namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// The workspace's view models, held to what the web client's screen tests say of the screen: what each address
/// shows, what the crew says, and how a conversation loads, sends, streams, stops, fails, and takes files.
/// </summary>
public sealed class TalkViewModelTests : WorkspaceTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid runId = Guid.Parse("0196f1a0-0000-7000-8000-000000000006");

    private static readonly Guid replyId = Guid.Parse("0196f1a0-0000-7000-8000-000000000007");

    private static readonly Guid helperId = Guid.Parse("0196f1a0-0000-7000-8000-000000000008");

    private static readonly Guid sideId = Guid.Parse("0196f1a0-0000-7000-8000-000000000009");

    private static readonly Guid failedRunId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a");

    #endregion

    #region Constructors

    public TalkViewModelTests()
        : base(new StreamPresence())
    {
        Server.Answer("GET", $"{Space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { Thread(null) });
        Main(Thread(null));
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample **answer**") });
    }

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null);

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active");

    private static BotSummary Helper { get; } =
        new(helperId, "Sample helper", "sample-helper", "Sample instructions", connectionId, "sample-default", "paused", botId);

    private static ThreadSummary Side { get; } = new(sideId, botId, "Sample side chat", false, DateTimeOffset.UnixEpoch, null);

    private static RunFailure Failure { get; } = new(failedRunId, FailureCauses.ModelUnreachable, "invalid_key: Sample report of the failure.");

    private ConversationViewModel MainPane => Talk.MainPane!;

    private ConversationViewModel SidePane => Talk.SidePane!;

    #endregion

    #region Not Ready

    [Fact]
    public async Task An_unreachable_conversation_keeps_the_server_check_as_its_next_step()
    {
        Server.Fail("GET", $"{Space}/threads/{threadId}/messages", new HttpRequestException("connection refused"));

        await Go("/");

        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        MainPane.LoadFailedNotice.Cause.Should().Be("Drover could not reach its server.");
        MainPane.List.Problem.Should().BeNull("the failure is the pane's state, not a line under the messages");
    }


    [Fact]
    public async Task With_no_model_the_workspace_says_so_and_links_to_connecting_one()
    {
        Server.Answer("GET", $"{Space}/model-connections", Array.Empty<ModelConnectionSummary>());
        Server.Answer("GET", $"{Space}/bots", Array.Empty<BotSummary>());

        await Go("/");

        await Until(() => Talk.Notice!.Title.Should().Be("No model is connected"));
        Talk.Notice!.Cause.Should().Be("A bot needs a model, and this space has none yet.");
        Talk.Notice!.ActionHref.Should().Be("/setup/model");
        Talk.NotReady.Should().Be(WorkspaceState.NoModel, "a phone shows the same words in the list");
        Talk.MainPane.Should().BeNull("there is no field to write in");
    }

    [Fact]
    public async Task With_a_model_and_no_bot_the_workspace_links_to_meeting_the_first_bot()
    {
        Server.Answer("GET", $"{Space}/bots", Array.Empty<BotSummary>());

        await Go("/");

        await Until(() => Talk.Notice!.Title.Should().Be("No bot yet"));
        Talk.Notice!.ActionLabel.Should().Be("Create your first bot");
        Talk.Notice!.ActionHref.Should().Be("/setup/bot");
    }

    [Fact]
    public async Task A_space_that_fails_to_load_says_why_and_loads_on_a_second_try()
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        await Go("/");
        await Until(() => Talk.Notice!.Title.Should().Be("Drover could not load this space"));
        Talk.Notice!.ActionHref.Should().BeNull("the action tries again here");

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        await Do(Talk.Retry);

        await Until(() => MainPane.Composer.FieldName.Should().Be("Message to Marshal"));
    }

    [Fact]
    public async Task Opening_a_panel_directly_keeps_loading_and_retry_visible_until_the_panel_is_ready()
    {
        var bots = Server.Hold("GET", $"{Space}/bots");
        await Go($"/talk/{botId}/screen");

        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Loading));
        Talk.At.Should().Be("main");
        TalkViewModel.LoadingHint.Should().Be("Loading your space.");
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        bots.SetResult();
        await Until(() => Talk.Notice!.Title.Should().Be("Drover could not load this space"));
        Talk.At.Should().Be("main");

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        await Do(Talk.Retry);
        await Until(() => Talk.At.Should().Be("side"));
        Talk.SideTitle.Should().Be("Screen");
    }

    [Fact]
    public async Task The_pages_title_is_drawn_while_the_space_is_still_loading_and_names_the_bot_once_it_has()
    {
        var bots = Server.Hold("GET", $"{Space}/bots");

        await Go("/");

        await Until(() => Talk.PageName.Should().Be("Drover"));
        bots.SetResult();
        await Until(() => Talk.PageName.Should().Be("Marshal · Drover"));
    }

    #endregion

    #region What Each Bot Has For You

    [Fact]
    public async Task Each_bot_says_in_words_what_it_has_for_the_person_and_the_tab_counts_who_waits()
    {
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { Thread(null) with { Attention = new(false, true, true, Failure) }, Side with { Attention = new(true, false, false, null) } });
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", new[] { new ThreadSummary(Guid.NewGuid(), helperId, null, false, DateTimeOffset.UnixEpoch, null, true, new(false, false, true, null)) });

        await Go("/");

        await Until(() => CrewNames().Should().Equal("Marshal, waiting for you", "Sample helper, paused, new"));
        CrewWords().Should().Equal("Waiting for you", "New");
        AttentionWords.Kind(Talk.ForYou[botId]).Should().Be(BadgeKind.Warn, "waiting has the attention color as well as the word");
        await Until(() => Talk.PageName.Should().Be("(1) Marshal · Drover"));
        Server.Sent("PUT", $"{Space}/threads/{threadId}/seen").Should().BeEmpty("on the start page a phone shows the crew: nothing was opened");
    }

    [Fact]
    public async Task A_signal_from_another_screen_changes_the_crew_without_a_reload()
    {
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper });
        var helperThread = new ThreadSummary(Guid.NewGuid(), helperId, null, false, DateTimeOffset.UnixEpoch, null, true, new(true, false, false, null));
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", new[] { helperThread });
        var signals = Server.Stream($"{Space}/stream");
        await Go("/");
        await Until(() => CrewWords().Should().Equal("Waiting for you"));

        Server.Answer("GET", $"{Space}/threads/{helperThread.Id}", helperThread with { Attention = new(false, false, false, null) });
        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, helperThread.Id, null));

        await Until(() => CrewWords().Should().BeEmpty("the approval was answered on another screen"));
    }

    [Fact]
    public async Task A_dropped_signal_connection_reads_the_crew_and_every_thread_again()
    {
        var signals = Server.Stream($"{Space}/stream");
        await Go("/");
        await Until(() => Talk.Crew.Should().ContainSingle());

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", new[] { new ThreadSummary(Guid.NewGuid(), helperId, null, false, DateTimeOffset.UnixEpoch, null, true, new(false, false, true, null)) });
        await signals.Send(new UserSignal(UserSignalKinds.Reconnected, null, null));

        await Until(() => CrewNames().Should().Equal("Marshal", "Sample helper, paused, new"));
    }

    [Fact]
    public async Task A_reconnected_signal_stream_reads_a_failed_conversation_again()
    {
        var signals = Server.Stream($"{Space}/stream");
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        var failed = MainPane;

        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample **answer**") });
        await signals.Send(new UserSignal(UserSignalKinds.Reconnected, null, null));

        await Until(() => failed.LoadFailed.Should().BeFalse("a connection that came back reads the conversation again"));
        await Until(() => failed.Messages.Should().HaveCount(2));
        failed.Composer.Disabled.Should().BeFalse();
        MainPane.Should().BeSameAs(failed);
        Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2);
    }

    [Fact]
    public async Task Opening_a_conversation_marks_it_seen_and_its_bot_has_nothing_new_while_it_is_open()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { Thread(null) with { Attention = new(false, false, true, null) } });
        Main(Thread(null) with { Attention = new(false, false, true, null) });
        Server.Answer("PUT", $"{Space}/threads/{threadId}/seen", HttpStatusCode.NoContent);

        await Go($"/talk/{botId}");

        await Until(() => Server.Sent("PUT", $"{Space}/threads/{threadId}/seen").Should().ContainSingle());
        CrewWords().Should().BeEmpty();
    }

    [Fact]
    public async Task The_start_pages_conversation_is_marked_seen_when_its_bot_is_chosen_and_stays_as_it_is()
    {
        Main(Thread(null) with { Attention = new(false, false, true, null) });
        Server.Answer("PUT", $"{Space}/threads/{threadId}/seen", HttpStatusCode.NoContent);
        await Go("/");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var shown = MainPane;
        Server.Sent("PUT", $"{Space}/threads/{threadId}/seen").Should().BeEmpty();

        await Go($"/talk/{botId}");

        await Until(() => Server.Sent("PUT", $"{Space}/threads/{threadId}/seen").Should().ContainSingle());
        MainPane.Should().BeSameAs(shown, "the conversation stays, the address changes");
        Server.Sent("PUT", $"{Space}/bots/{botId}/main-thread").Should().ContainSingle("it was not loaded again");
    }

    [Fact]
    public async Task A_side_chat_says_what_it_has_so_a_word_in_the_crew_can_be_traced_to_it()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { Thread(null), Side with { Attention = new(false, false, true, null) } });

        await Go($"/talk/{botId}/side");

        await Until(() => AttentionWords.Word(Talk.SideChats.Single().Attention).Should().Be("New"));
    }

    #endregion

    #region Changes Made Elsewhere

    [Fact]
    public async Task A_notification_reply_reaches_the_open_conversation_and_its_run_is_followed()
    {
        var signals = Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Do(() =>
        {
            MainPane.Composer.Text = "A half written sample";
            return Task.CompletedTask;
        });
        var shown = MainPane;
        var events = ChangeElsewhere(
            Said("user", "Sample question"),
            Said("assistant", "Sample answer"),
            Said("user", "Sample reply from the notification"));

        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));

        await Until(() =>
        {
            MainPane.Messages.Select(message => message.Text).Should().Contain("Sample reply from the notification");
            RunWatched().Should().BeTrue();
            MainPane.Working.Should().BeTrue();
        });
        MainPane.Should().BeSameAs(shown);
        MainPane.Composer.Text.Should().Be("A half written sample");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
    }

    [Fact]
    public async Task A_reconnected_signal_stream_brings_the_open_conversation_up_to_date()
    {
        var signals = Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var events = ChangeElsewhere(
            Said("user", "Sample question"),
            Said("assistant", "Sample answer"),
            Said("user", "Sample reply while disconnected"));

        await signals.Send(new UserSignal(UserSignalKinds.Reconnected, null, null));

        await Until(() =>
        {
            MainPane.Messages.Select(message => message.Text).Should().Contain("Sample reply while disconnected");
            RunWatched().Should().BeTrue();
        });
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
    }

    [Fact]
    public async Task Showing_the_app_again_brings_the_open_conversation_up_to_date()
    {
        Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Presence!.Hide();
        var events = ChangeElsewhere(
            Said("user", "Sample question"),
            Said("assistant", "Sample answer"),
            Said("user", "Sample reply while away"));
        Server.Stream($"{Space}/stream");

        Presence.Show();

        await Until(() =>
        {
            MainPane.Messages.Select(message => message.Text).Should().Contain("Sample reply while away");
            RunWatched().Should().BeTrue();
        });
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
    }

    [Fact]
    public async Task A_second_remote_change_during_the_first_read_is_read_after_it()
    {
        var signals = Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        MessageResponse[] first =
        [
            Said("user", "Sample question"),
            Said("assistant", "Sample answer"),
            Said("user", "First remote reply"),
        ];
        MessageResponse[] second =
        [
            .. first,
            Said("user", "Second remote reply"),
        ];
        Server.AnswerInTurn("GET", $"{Space}/threads/{threadId}/messages", first, second);
        var reading = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        Main(Thread(null) with { LastActivityAt = DateTimeOffset.UnixEpoch.AddMinutes(1) });

        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2));
        Main(Thread(null) with { LastActivityAt = DateTimeOffset.UnixEpoch.AddMinutes(2) });
        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));
        reading.SetResult();

        await Until(() => MainPane.Messages.Select(message => message.Text).Should().Contain("Second remote reply"));
        Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(3);
    }

    [Fact]
    public async Task A_seen_signal_updates_the_crew_without_reading_the_open_conversation_again()
    {
        var signals = Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var reads = Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Count();

        await signals.Send(new UserSignal(UserSignalKinds.ThreadSeen, threadId, null));
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}").Should().NotBeEmpty());

        Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(reads);
    }

    [Fact]
    public async Task A_remote_read_cannot_overwrite_a_message_sent_while_it_waits()
    {
        var signals = Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var remote = Said("user", "Remote reply");
        MessageResponse[] remoteMessages =
            [Said("user", "Sample question"), Said("assistant", "Sample answer"), remote];
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", remoteMessages);
        var reading = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        Main(Thread(null) with { LastActivityAt = DateTimeOffset.UnixEpoch.AddMinutes(1) });
        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2));
        var sent = Said("user", "Local reply");
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages", new SendMessageResponse(sent, Run("queued")),
            HttpStatusCode.Accepted);
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        var sending = Ui.Run(() =>
        {
            MainPane.Composer.Text = "Local reply";
            return MainPane.Composer.Send();
        });

        reading.SetResult();
        await sending;

        await Until(() => MainPane.Messages.Select(message => message.Text)
            .Should().Contain(["Remote reply", "Local reply"]));
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
    }

    [Fact]
    public async Task A_remote_read_cannot_restore_the_reply_replaced_by_a_version_switch()
    {
        var question = Said("user", "Sample question");
        var second = Said("assistant", "Second sample answer") with { Id = replyId };
        var first = Said("assistant", "First sample answer");
        MessageResponse[] secondShown = [question, second with { Versions = [first.Id, replyId] }];
        MessageResponse[] firstShown = [question, first with { Versions = [first.Id, replyId] }];
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", secondShown);
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages/{first.Id}/show", HttpStatusCode.NoContent);
        var signals = Server.Stream($"{Space}/stream");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Server.AnswerInTurn(
            "GET",
            $"{Space}/threads/{threadId}/messages",
            secondShown,
            firstShown);
        var reading = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        Main(Thread(null) with { LastActivityAt = DateTimeOffset.UnixEpoch.AddMinutes(1) });
        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2));
        var switching = Ui.Run(() => MainPane.List.ShowVersion(first.Id));

        reading.SetResult();
        await switching;

        await Until(() => MainPane.Messages.Select(message => message.Text)
            .Should().Equal("Sample question", "First sample answer"));
    }

    [Fact]
    public async Task A_remote_version_switch_drops_the_previous_completed_reply()
    {
        var signals = Server.Stream($"{Space}/stream");
        var events = RunStarts();
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Send(() => MainPane, "Another sample question");

        var question = Said("user", "Another sample question");
        var previous = Said("assistant", "Previous answer") with { Id = replyId, RunId = runId };
        var replacement = Said("assistant", "Replacement answer");
        MessageResponse[] before = [question, previous, Said("user", "Stored follow-up")];
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", before);
        Main(Thread(null));
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.MessageCompleted, new MessageCompleted(previous)));
        await events.Send(Event(3, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)));
        await events.End();
        await Until(() => MainPane.Messages.Select(message => message.Text).Should()
            .Equal("Another sample question", "Previous answer", "Stored follow-up"));

        MessageResponse[] after = [question, replacement with { Versions = [replyId, replacement.Id] }];
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", after);
        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, threadId, null));

        await Until(() => MainPane.Messages.Select(message => message.Text).Should()
            .Equal("Another sample question", "Replacement answer"));
    }

    #endregion

    #region Opening

    [Fact]
    public async Task Opening_a_conversation_makes_it_once_and_reads_it_once()
    {
        var sandbox = Server.Hold("GET", $"{Space}/bots/{botId}/sandbox");
        var messages = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        HashSet<ConversationViewModel> made = [];
        Talk.PropertyChanged += (_, _) =>
        {
            if (Talk.MainPane is { } pane)
            {
                made.Add(pane);
            }
        };

        await Go($"/talk/{botId}");
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().ContainSingle());
        messages.SetResult();
        sandbox.SetResult();

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        made.Should().ContainSingle("the screen changes several times while it opens, and keeps the one conversation");
        Server.Sent("PUT", $"{Space}/bots/{botId}/main-thread").Should().ContainSingle();
        Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().ContainSingle();
    }

    [Fact]
    public async Task A_conversation_starts_loading_before_the_question_of_the_bots_screen_is_answered()
    {
        var bots = Server.Hold("GET", $"{Space}/bots");
        var sandbox = Server.Hold("GET", $"{Space}/bots/{botId}/sandbox");
        await Go($"/talk/{botId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Loading));

        bots.SetResult();

        await Until(() => Talk.MainPane!.Messages.Should().HaveCount(2));
        sandbox.SetResult();
    }

    [Fact]
    public async Task A_conversation_with_messages_says_it_is_loading_until_they_arrive_and_never_that_nothing_was_said()
    {
        var messages = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        List<(bool Loading, int Count, bool Empty)> seen = [];
        ConversationViewModel? followed = null;
        Talk.PropertyChanged += (_, _) =>
        {
            if (Talk.MainPane is { } pane && followed is null)
            {
                followed = pane;
                Record(pane);
                pane.List.PropertyChanged += (_, _) => Record(pane);
            }
        };

        await Go($"/talk/{botId}");
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().ContainSingle());
        MainPane.Loading.Should().BeTrue();
        MainPane.List.IsEmpty.Should().BeFalse("nothing is known to be said or not while the conversation is read");
        messages.SetResult();

        await Until(() => seen.Should().Contain((false, 2, false)));
        seen.Should().NotContain(state => state.Empty, "the empty state is never shown for a conversation that has messages");
        seen.Should().NotContain(state => !state.Loading && state.Count == 0, "it says it is loading until the messages are there");
        seen.Should().Equal(seen.OrderByDescending(state => state.Loading), "loading comes first, then the messages");

        void Record(ConversationViewModel pane) => seen.Add((pane.Loading, pane.List.Messages.Count, pane.List.IsEmpty));
    }

    [Fact]
    public async Task A_conversation_with_nothing_said_says_so_once_it_has_been_read()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", Array.Empty<MessageResponse>());

        await Go($"/talk/{botId}");

        await Until(() => MainPane.Loading.Should().BeFalse());
        await Until(() => MainPane.List.IsEmpty.Should().BeTrue());
        MainPane.List.StartHint.Should().Be("No messages yet. Write to Marshal below.");
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("main-thread")]
    public async Task A_conversation_that_fails_to_load_says_so_and_never_that_nothing_was_said(string request)
    {
        if (request is "messages")
        {
            Server.Answer("GET", $"{Space}/threads/{threadId}/messages", HttpStatusCode.InternalServerError);
        }
        else
        {
            Server.Answer("PUT", $"{Space}/bots/{botId}/main-thread", HttpStatusCode.InternalServerError);
        }

        List<(bool Failed, bool Empty)> shown = [];
        Talk.PropertyChanged += (_, _) =>
        {
            if (Talk.MainPane is { } pane)
            {
                shown.Add((pane.LoadFailed, pane.List.IsEmpty));
                pane.List.PropertyChanged += (_, _) => shown.Add((pane.LoadFailed, pane.List.IsEmpty));
            }
        };

        await Go($"/talk/{botId}");

        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        MainPane.LoadFailedNotice.Title.Should().Be("Drover could not load this conversation");
        MainPane.LoadFailedNotice.Cause.Should().Be("The server failed to answer. Try again, and check the server log if it keeps happening.");
        MainPane.LoadFailedNotice.ActionLabel.Should().Be("Try again");
        MainPane.LoadFailedNotice.ActionHref.Should().BeNull("the action is here, not a link away");
        MainPane.List.Problem.Should().BeNull("the failure is the pane's state, not a line under the messages");
        MainPane.Composer.Disabled.Should().BeTrue("nothing can be written into a conversation that was not read");
        shown.Should().Contain(state => state.Failed);
        shown.Should().NotContain(
            state => !state.Failed && state.Empty,
            "the list's empty hint is not what a conversation that was never read shows");
    }

    [Fact]
    public async Task Try_again_reads_the_conversation_and_shows_its_messages()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        var failed = MainPane;

        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample **answer**") });
        await Do(failed.Reload);

        await Until(() => failed.Messages.Should().HaveCount(2));
        failed.LoadFailed.Should().BeFalse();
        failed.Composer.Disabled.Should().BeFalse();
        MainPane.Should().BeSameAs(failed, "the conversation stays, it is read again");
        Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2);
    }

    [Fact]
    public async Task The_disabled_message_field_says_it_will_be_available_when_the_conversation_has_loaded()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");

        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        MainPane.Composer.Disabled.Should().BeTrue();
        MainPane.Composer.FieldDescription.Should().Be("Available when the conversation has loaded.");

        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { Said("assistant", "Sample answer") });
        await Do(MainPane.Reload);

        await Until(() => MainPane.Messages.Should().ContainSingle());
        await Until(() => MainPane.Composer.FieldDescription.Should().BeEmpty("the description leaves with the failure"));
        MainPane.LoadFailed.Should().BeFalse();
        MainPane.Composer.Disabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_second_try_while_the_first_is_loading_asks_the_server_once()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.LoadFailed.Should().BeTrue());

        var held = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        var retry = Ui.Run(() => MainPane.Reload());
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2));
        await Until(() => MainPane.Loading.Should().BeTrue());

        await Do(() =>
        {
            _ = MainPane.Reload();
            return Task.CompletedTask;
        });

        Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().HaveCount(2, "a second press while loading asks nothing");
        held.SetResult();
        await retry;
    }

    [Fact]
    public async Task A_retry_that_fails_again_shows_the_new_sentence_and_keeps_the_way_to_try_again()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        var first = MainPane.LoadFailedNotice;

        Server.TimeOut("GET", $"{Space}/threads/{threadId}/messages");
        await Do(MainPane.Reload);

        await Until(() => MainPane.LoadFailedNotice.Cause.Should().Be("Drover's server did not answer in time."));
        MainPane.LoadFailedNotice.Cause.Should().NotBe(first.Cause);
        MainPane.LoadFailedNotice.Title.Should().Be("Drover could not load this conversation");
        MainPane.LoadFailedNotice.ActionLabel.Should().Be("Try again");
        MainPane.Composer.Disabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_handed_work_conversation_that_fails_to_load_shows_the_same_failed_state()
    {
        var workThread = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d2");
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{Space}/threads/{workThread}", new ThreadSummary(workThread, helperId, null, false, DateTimeOffset.UnixEpoch, null));
        Server.Answer("GET", $"{Space}/threads/{workThread}/messages", HttpStatusCode.InternalServerError);

        await Go(Addresses.Work(botId, helperId, workThread));

        await Until(() => Talk.WorkPane!.LoadFailed.Should().BeTrue());
        var work = Talk.WorkPane!;
        work.LoadFailedNotice.Title.Should().Be("Drover could not load this conversation");
        work.ReadOnly.Should().BeTrue();
        Server.Sent("GET", $"{Space}/threads/{workThread}/messages").Should().ContainSingle();
    }

    #endregion

    #region Reading

    [Fact]
    public async Task A_dropped_run_stream_says_it_is_reconnecting_and_stops_saying_so_once_through_even_with_nothing_new()
    {
        Main(Thread(runId));
        var first = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await first.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await first.Send(Event(2, RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_approval", null, 1, null)));
        await Until(() => MainPane.Waiting.Should().BeTrue());

        // The run waits on the person, so the stream that comes back has nothing to send.
        Server.Stream($"{Space}/runs/{runId}/events");
        var dropped = false;
        MainPane.PropertyChanged += (_, _) => dropped |= MainPane.Notice == "The connection dropped. Reconnecting.";
        await first.End();

        await Until(() =>
        {
            dropped.Should().BeTrue("the screen says the connection dropped while it is away");
            Server.Addresses.Count(address => address.StartsWith($"GET {Space}/runs/{runId}/events", StringComparison.Ordinal)).Should().Be(2);
            MainPane.Notice.Should().BeNull("the stream is through again");
        });
    }

    [Fact]
    public async Task The_start_page_lists_the_crew_as_a_tree_and_opens_the_leads_main_conversation()
    {
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", Array.Empty<ThreadSummary>());

        await Go("/");

        await Until(() => ChatRows.CrewRows(Talk.Crew).Select(row => Addresses.Talk(row.Bot.Id)).Should().Equal($"/talk/{botId}", $"/talk/{helperId}"));
        Talk.At.Should().Be("crew", "the crew has the page's heading, Conversations");
        Talk.Shown!.Id.Should().Be(botId, "the lead is the bot being talked to");
        var beneath = Talk.Crew.Single().Children.Single().Bot;
        beneath.Name.Should().Be("Sample helper");
        ChatRows.IsPaused(beneath).Should().BeTrue();
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Server.Sent("PUT", $"{Space}/bots/{botId}/main-thread").Should().ContainSingle("the main conversation is opened through the bot");
    }

    [Fact]
    public async Task Choosing_a_bot_shows_its_main_conversation_under_its_name_with_a_way_back_to_the_crew()
    {
        await Go($"/talk/{botId}");

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Talk.At.Should().Be("main");
        Talk.Shown!.Name.Should().Be("Marshal");
        MainPane.Messages[0].Should().Match<ChatMessage>(message => message.FromPerson && message.Text == "Sample question");
        MainPane.Messages[1].Text.Should().Be("Sample **answer**");
        Talk.CanShowRail.Should().BeFalse("side chats stay out of the way until they are asked for");
        Talk.SidePane.Should().BeNull();
    }

    [Fact]
    public async Task A_bot_that_is_gone_says_so_and_leads_back_to_conversations()
    {
        await Go($"/talk/{helperId}");

        await Until(() => Talk.Notice!.Title.Should().Be("Bot not found"));
        Talk.Notice!.ActionHref.Should().Be("/");
        Talk.MainPane.Should().BeNull();
    }

    [Fact]
    public async Task An_address_kept_from_before_goes_to_the_bots_main_conversation_or_its_side_chat()
    {
        await Go($"/threads/{threadId}");
        await Until(() => Address.Should().Be($"talk/{botId}"));

        Server.Answer("GET", $"{Space}/threads/{sideId}", Side);
        await Go($"/threads/{sideId}");
        await Until(() => Address.Should().Be($"talk/{botId}/side/{sideId}"));
    }

    [Fact]
    public async Task A_second_try_that_answers_after_the_person_moved_on_does_not_replace_where_they_went()
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        await Go($"/threads/{threadId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Failed));

        var bots = Server.Hold("GET", $"{Space}/bots");
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        var retry = Ui.Run(Talk.Retry);

        // The person leaves the conversations for another screen, which closes this one.
        await Go("/tools");
        await Ui.Run(Talk.Dispose);
        bots.SetResult();
        await retry;

        Navigator.Current.Should().Be("/tools");
    }

    [Fact]
    public async Task A_second_try_that_answers_after_another_conversation_was_opened_does_not_replace_it()
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        await Go($"/threads/{threadId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Failed));

        var thread = Server.Hold("GET", $"{Space}/threads/{threadId}");
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{Space}/threads/{threadId}", Thread(null));
        var retry = Ui.Run(Talk.Retry);
        await Until(() => Server.Addresses.Should().Contain($"GET {Space}/threads/{threadId}"));

        await Go(Addresses.SideChats(botId));
        thread.SetResult();
        await retry;

        Navigator.Current.Should().Be(Addresses.SideChats(botId));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_second_try_whose_lookup_fails_after_another_conversation_was_opened_leaves_it_alone(
        HttpStatusCode failure)
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        await Go($"/threads/{threadId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Failed));

        var thread = Server.Hold("GET", $"{Space}/threads/{threadId}");
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{Space}/threads/{threadId}", failure);
        var retry = Ui.Run(Talk.Retry);
        await Until(() => Server.Addresses.Should().Contain($"GET {Space}/threads/{threadId}"));

        await Go(Addresses.SideChats(botId));
        thread.SetResult();
        await retry;

        Navigator.Current.Should().Be(Addresses.SideChats(botId));
        Talk.FormerMissing.Should().BeFalse();
        Talk.NotReady.Should().NotBe(WorkspaceState.Failed);
    }

    [Fact]
    public async Task A_late_not_found_for_an_older_kept_address_keeps_the_notice_of_the_current_one()
    {
        var older = Guid.Parse("0196f1a0-0000-7000-8000-00000000000b");
        var current = Guid.Parse("0196f1a0-0000-7000-8000-00000000000c");
        var olderLookup = Server.Hold("GET", $"{Space}/threads/{older}");
        Server.Answer("GET", $"{Space}/threads/{older}", HttpStatusCode.NotFound);
        Server.Answer("GET", $"{Space}/threads/{current}", HttpStatusCode.NotFound);

        await Go($"/threads/{older}");
        await Until(() => Server.Addresses.Should().Contain($"GET {Space}/threads/{older}"));
        await Go($"/threads/{current}");
        await Until(() => Talk.FormerMissing.Should().BeTrue());

        olderLookup.SetResult();
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await Ui.Run(() => { });

        Talk.FormerMissing.Should().BeTrue("the late answer was about the older address");
    }

    [Fact]
    public async Task A_second_try_that_stays_on_a_kept_address_still_goes_to_its_conversation()
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        await Go($"/threads/{threadId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Failed));

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        await Do(Talk.Retry);

        await Until(() => Address.Should().Be($"talk/{botId}"));
    }

    [Fact]
    public async Task An_address_kept_from_before_whose_conversation_is_gone_says_so()
    {
        var gone = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a");
        Server.Answer("GET", $"{Space}/threads/{gone}", HttpStatusCode.NotFound);

        await Go($"/threads/{gone}");

        await Until(() => Talk.Notice!.Title.Should().Be("Conversation not found"));
        Talk.Notice!.ActionHref.Should().Be("/");
    }

    [Fact]
    public async Task A_return_from_a_tool_servers_sign_in_goes_on_to_the_tool_connections()
    {
        var connection = Guid.Parse("0196f1a0-0000-7000-8000-0000000000aa");

        await Go($"/?tool-sign-in=connected&connection={connection}");

        await Until(() => Navigator.Current.Should().Be(Addresses.ToolSignIn("connected", connection.ToString())));
        Navigator.CanGoBack.Should().BeTrue("only the return address was replaced");
    }

    #endregion

    #region Screen

    [Fact]
    public async Task A_bot_asking_for_its_screen_is_taken_over_from_its_card_and_handed_back_beside_the_screen()
    {
        var callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c9");
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        Server.Answer("POST", $"{Space}/tool-calls/{callId}/takeover", Run("waiting"));
        Server.Answer("POST", $"{Space}/tool-calls/{callId}/handback", Run("waiting"));
        Main(Thread(runId));
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.ToolRequested, new ToolRequested(callId, "computer.handover", "computer.handover", """{"reason":"Sign in with your passkey."}""", "allow")));
        await events.Send(Event(3, RunEventTypes.ToolApprovalRequested, new ToolApprovalRequested(callId, "r1", "c1")));
        await events.Send(Event(4, RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_takeover", null, 1, null)));

        await Until(() =>
        {
            Waiting().Should().Match<Takeover>(takeover => takeover.ToolCallId == callId && takeover.Phase == TakeoverPhase.Asked);
            MainPane.Thinking.Should().BeFalse("nothing thinks while the bot waits for the person");
            MainPane.Notice.Should().BeNull();
        });
        var focused = 0;
        Talk.RailCloseFocusRequested += () => focused++;
        var refused = "not asked";
        await Do(async () => refused = await MainPane.Takeovers.OpenScreen!(Waiting()!));

        refused.Should().BeNull();
        await Until(() => Address.Should().Be($"talk/{botId}/screen?held={callId}"));
        Server.Sent("POST", $"{Space}/tool-calls/{callId}/takeover").Should().ContainSingle();
        await Until(() => Talk.HeldTakeover!.ToolCallId.Should().Be(callId));
        await Until(() => MainPane.Takeovers.HeldCallId.Should().Be(callId, "one set of controls, beside the screen"));
        Talk.Side.Should().Be(SideView.Screen);

        await Do(async () =>
        {
            (await MainPane.Takeovers.HandBack!(callId, true, null)).Should().BeNull();
            Talk.HandedBack(callId);
        });

        await Until(() => Talk.HeldTakeover.Should().BeNull());
        Server.Sent("POST", $"{Space}/tool-calls/{callId}/handback").Single().Should().Contain("\"done\":true");

        // The focus moves to the screen's close control, the one way on once the controls are gone.
        focused.Should().Be(1);
        Talk.CloseRailName.Should().Be("Close the screen");
    }

    [Fact]
    public async Task After_a_reload_the_person_still_holds_the_screen_they_took_over()
    {
        var callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c6");
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        Main(Thread(runId));
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}/screen?held={callId}");
        await events.Send(Event(1, RunEventTypes.ToolRequested, new ToolRequested(callId, "computer.handover", "computer.handover", """{"reason":"Sign in."}""", "allow")));
        await events.Send(Event(2, RunEventTypes.ToolApprovalRequested, new ToolApprovalRequested(callId, "r1", "c1")));
        await events.Send(Event(3, RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_takeover", null, 1, null)));

        await Until(() => Talk.HeldTakeover!.ToolCallId.Should().Be(callId));
        await Until(() => MainPane.Takeovers.HeldCallId.Should().Be(callId, "the screen is held, so it is not offered again"));
        Server.Sent("POST", $"{Space}/tool-calls/{callId}/takeover").Should().BeEmpty();
    }

    [Fact]
    public async Task Taking_over_with_the_screen_already_open_turns_control_on()
    {
        var callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c7");
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        Server.Answer("POST", $"{Space}/tool-calls/{callId}/takeover", Run("waiting"));
        Main(Thread(runId));
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}/screen");
        await events.Send(Event(1, RunEventTypes.ToolRequested, new ToolRequested(callId, "computer.handover", "computer.handover", """{"reason":"Sign in."}""", "allow")));
        await events.Send(Event(2, RunEventTypes.ToolApprovalRequested, new ToolApprovalRequested(callId, "r1", "c1")));
        await events.Send(Event(3, RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_takeover", null, 1, null)));
        await Until(() => Waiting().Should().NotBeNull());
        Talk.HeldTakeover.Should().BeNull();

        await Do(() => MainPane.Takeovers.OpenScreen!(Waiting()!));

        await Until(() => Talk.HeldTakeover!.ToolCallId.Should().Be(callId));
    }

    [Fact]
    public async Task Opening_the_screen_by_its_link_does_not_take_over_a_waiting_request()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        Main(Thread(null));
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", (List<MessageResponse>)[Handover()]);

        await Go($"/talk/{botId}/screen");

        await Until(() => Waiting()!.Phase.Should().Be(TakeoverPhase.Asked));
        Talk.HeldTakeover.Should().BeNull();
        MainPane.Takeovers.HeldCallId.Should().BeNull();
    }

    [Fact]
    public async Task A_step_back_closes_a_rail_opened_from_the_conversation_and_then_leaves_it()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        await Go($"/talk/{botId}");
        await Go($"/talk/{botId}/screen");
        await Until(() => Talk.CanShowRail.Should().BeTrue());

        await StepBack(BackStep.Closed);

        await Until(() => Talk.Side.Should().Be(SideView.Closed));
        Navigator.Current.Should().Be($"/talk/{botId}");
        Navigator.StepBack().Should().Be(BackStep.WentBack, "with the rail closed, back leaves the conversation");
        Navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task A_step_back_closes_a_rail_opened_directly_into_its_conversation()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        Navigator.StartAt($"/talk/{botId}/screen");
        await Do(() => Talk.Open(new(botId, "screen")));
        await Until(() => Talk.CanShowRail.Should().BeTrue());

        await StepBack(BackStep.Closed);

        await Until(() => Talk.Side.Should().Be(SideView.Closed));
        Navigator.Current.Should().Be($"/talk/{botId}");
        Navigator.CanStepBack.Should().BeFalse("the rail leaves no step behind, so the next back leaves the app");
    }

    [Fact]
    public async Task A_step_back_in_a_side_chat_goes_to_the_list_of_them()
    {
        await Go($"/talk/{botId}/side/new");

        await StepBack(BackStep.Closed);

        Navigator.Current.Should().Be(Addresses.SideChats(botId));
    }

    [Fact]
    public async Task A_bot_with_a_screen_offers_it_beside_the_conversation()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));

        await Go($"/talk/{botId}/screen");

        await Until(() => Talk.RailName.Should().Be("Marshal's screen"));
        Talk.SideTitle.Should().Be("Screen");
        Talk.CloseRailName.Should().Be("Close the screen");
        Talk.RailCloses.Should().BeTrue();
        Talk.OffersScreen.Should().BeTrue();
        Talk.HasWideRail.Should().BeTrue("a screen needs room to be read");
        await Until(() => MainPane.Messages.Should().HaveCount(2, "the conversation stays beside the screen"));
        await Until(() => MainPane.Takeovers.Access.Should().Be(ScreenAccess.Usable));
    }

    [Fact]
    public async Task A_bot_whose_sandbox_has_no_screen_offers_no_way_to_one()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: false));

        await Go($"/talk/{botId}");

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Until(() => MainPane.Takeovers.Access.Should().Be(ScreenAccess.Missing));
        Talk.OffersScreen.Should().BeFalse();
        MainPane.Takeovers.SetupHref.Should().Be($"/bots/{botId}");
    }

    [Fact]
    public async Task A_person_who_may_not_use_the_screen_is_told_so_and_can_still_talk()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", HttpStatusCode.Forbidden);

        await Go($"/talk/{botId}/screen");

        await Until(() => MainPane.Takeovers.Access.Should().Be(ScreenAccess.NotPermitted));
        MainPane.ReadOnly.Should().BeFalse();
        await Until(() => MainPane.Composer.Disabled.Should().BeFalse());
    }

    [Fact]
    public async Task A_space_tried_again_after_it_failed_to_load_asks_about_the_bots_screen()
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Failed));

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        await Do(Talk.Retry);

        await Until(() => MainPane.Takeovers.Access.Should().Be(ScreenAccess.Usable));
        Talk.OffersScreen.Should().BeTrue();
    }

    [Fact]
    public async Task A_takeover_already_waiting_when_the_space_is_tried_again_offers_its_buttons()
    {
        Server.Answer("GET", $"{Space}/bots", HttpStatusCode.InternalServerError);
        Main(Thread(null));
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", (List<MessageResponse>)[Handover()]);
        await Go($"/talk/{botId}");
        await Until(() => Talk.NotReady.Should().Be(WorkspaceState.Failed));

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        await Do(Talk.Retry);

        await Until(() => Waiting()!.Phase.Should().Be(TakeoverPhase.Asked));
        await Until(() => MainPane.Takeovers.Access.Should().Be(ScreenAccess.Usable));
        Talk.OffersScreen.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_ask_for_a_bots_screen_is_asked_again_on_the_next_visit_to_that_bot()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Talk.OffersScreen.Should().BeFalse("the ask failed, so the screen is not known");

        Server.Answer("GET", $"{Space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        await Go($"/talk/{botId}/side/new");

        await Until(() => Talk.OffersScreen.Should().BeTrue());
        Server.Sent("GET", $"{Space}/bots/{botId}/sandbox").Should().HaveCount(2);
    }

    #endregion

    #region Beside The Conversation

    [Fact]
    public async Task The_conversation_menu_opens_the_same_summary_as_the_wide_layout_link()
    {
        await Go($"/talk/{botId}");
        await Until(() => Talk.Shown.Should().NotBeNull());

        await Do(() =>
        {
            Talk.OpenSide(Addresses.Summary(botId));
            return Task.CompletedTask;
        });

        Address.Should().Be($"talk/{botId}/summary");
    }

    [Fact]
    public async Task The_summary_opens_beside_the_main_conversation_under_its_own_names()
    {
        await Go($"/talk/{botId}/summary");

        await Until(() => Talk.RailName.Should().Be("Summary of the conversation with Marshal"));
        Talk.SideTitle.Should().Be("Summary");
        Talk.CloseRailName.Should().Be("Close the summary");
        Talk.Side.Should().Be(SideView.Summary);
        Talk.PageName.Should().Be("Summary · Marshal · Drover");
        await Until(() => MainPane.Messages.Should().HaveCount(2, "the main conversation stays beside it"));
    }

    [Theory]
    [InlineData("memory", SideView.Memory, "Memory", "Marshal's notes about you", "Close memory")]
    [InlineData("files", SideView.Files, "Files", "Files exchanged with Marshal", "Close the files")]
    [InlineData("activity", SideView.Activity, "Activity", "Activity of Marshal and its crew in a run", "Close the activity")]
    public async Task Each_thing_beside_the_conversation_has_its_title_its_name_and_its_way_to_close(
        string rail, SideView side, string title, string name, string close)
    {
        await Go($"/talk/{botId}/{rail}");

        await Until(() => Talk.Side.Should().Be(side));
        await Until(() => Talk.RailName.Should().Be(name));
        Talk.SideTitle.Should().Be(title);
        Talk.CloseRailName.Should().Be(close);
        Talk.At.Should().Be("side");
    }

    [Theory]
    [InlineData("summary", SideView.Summary)]
    [InlineData("memory", SideView.Memory)]
    [InlineData("files", SideView.Files)]
    [InlineData("activity", SideView.Activity)]
    [InlineData("side", SideView.List)]
    [InlineData("side/new", SideView.List)]
    public async Task Escape_closes_the_rail_and_the_focus_goes_back_to_the_way_that_opened_it(string rail, SideView way)
    {
        await Go($"/talk/{botId}/{rail}");
        await Until(() => Talk.CanShowRail.Should().BeTrue());
        List<SideView?> openers = [];
        Talk.RailOpenerFocusRequested += openers.Add;

        var closed = false;
        await Do(() =>
        {
            closed = Talk.CloseRailFromKeyboard();
            return Task.CompletedTask;
        });

        closed.Should().BeTrue();
        Address.Should().Be($"talk/{botId}");
        await Until(() => openers.Should().Equal(way));
        Talk.Side.Should().Be(SideView.Closed);
        Talk.At.Should().Be("main");
        await Until(() => MainPane.Messages.Should().HaveCount(2, "the main conversation is as it was"));
    }

    [Fact]
    public async Task Escape_with_nothing_beside_the_conversation_is_left_to_something_else()
    {
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        List<SideView?> openers = [];
        Talk.RailOpenerFocusRequested += openers.Add;

        var closed = true;
        await Do(() =>
        {
            closed = Talk.CloseRailFromKeyboard();
            return Task.CompletedTask;
        });

        closed.Should().BeFalse();
        Address.Should().Be($"talk/{botId}");
        openers.Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_in_a_side_chat_with_a_draft_keeps_the_rail_and_the_draft()
    {
        await Go($"/talk/{botId}/side/new");
        await Until(() => Talk.SidePane.Should().NotBeNull());
        List<SideView?> openers = [];
        Talk.RailOpenerFocusRequested += openers.Add;

        var closed = true;
        await Do(() =>
        {
            Talk.SidePane!.Composer.Text = "Half-written side chat message";
            closed = Talk.CloseRailFromKeyboard();
            return Task.CompletedTask;
        });

        closed.Should().BeFalse();
        Address.Should().Be($"talk/{botId}/side/new");
        Talk.Side.Should().Be(SideView.New);
        Talk.SidePane!.Composer.Text.Should().Be("Half-written side chat message");
        openers.Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_in_a_side_chat_with_an_attached_file_keeps_the_rail_and_the_file()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/bots/{botId}/threads", Side with { Title = null }, HttpStatusCode.Created);
        Server.Answer("GET", $"{Space}/threads/{sideId}/attachment-rules", new AttachmentRules(1024, 10, true, true));
        Server.Answer("POST", $"{Space}/threads/{sideId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        await Go($"/talk/{botId}/side/new");
        await Until(() => Talk.SidePane.Should().NotBeNull());

        await Attach(() => Talk.SidePane!, new SamplePickedFile("notes.txt", "one"));
        await Until(() => Talk.SidePane!.Composer.Attachments.Should().ContainSingle());

        var closed = true;
        await Do(() =>
        {
            closed = Talk.CloseRailFromKeyboard();
            return Task.CompletedTask;
        });

        closed.Should().BeFalse();
        Talk.SidePane!.Composer.Attachments.Should().ContainSingle();
        Talk.CanShowRail.Should().BeTrue();
    }

    [Theory]
    [InlineData("memory", SideView.Memory)]
    [InlineData("side", SideView.List)]
    public async Task Escape_in_a_non_composer_rail_closes_the_rail(string path, SideView opener)
    {
        await Go($"/talk/{botId}/{path}");
        await Until(() => Talk.Side.Should().Be(opener));
        List<SideView?> openers = [];
        Talk.RailOpenerFocusRequested += openers.Add;

        var closed = false;
        await Do(() =>
        {
            closed = Talk.CloseRailFromKeyboard();
            return Task.CompletedTask;
        });

        closed.Should().BeTrue();
        Address.Should().Be($"talk/{botId}");
        Talk.CanShowRail.Should().BeFalse();
        openers.Should().Equal([opener]);
    }

    [Theory]
    [InlineData(SideView.Summary, SideView.Summary)]
    [InlineData(SideView.Chat, SideView.List)]
    [InlineData(SideView.New, SideView.List)]
    [InlineData(SideView.Work, null)]
    [InlineData(SideView.Closed, null)]
    public void Each_thing_beside_the_conversation_is_opened_by_its_way_in_the_header(SideView side, SideView? way) =>
        side.Way().Should().Be(way);

    [Fact]
    public async Task An_earlier_run_opens_in_the_activity_rail_by_its_own_address()
    {
        await Go(Addresses.Activity(botId, runId));

        await Until(() => Talk.Side.Should().Be(SideView.Activity));
        Talk.Address.ActivityRunId.Should().Be(runId);
        Talk.HasWideRail.Should().BeTrue();
    }

    [Fact]
    public async Task A_bots_side_of_handed_work_opens_to_read_and_one_whose_bot_is_gone_says_so()
    {
        var workThread = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");
        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", Array.Empty<ThreadSummary>());
        Server.Answer("GET", $"{Space}/threads/{workThread}", new ThreadSummary(workThread, helperId, null, false, DateTimeOffset.UnixEpoch, null));
        Server.Answer("GET", $"{Space}/threads/{workThread}/messages", new[] { Said("user", "Sample task") with { ThreadId = workThread } });

        await Go(Addresses.Work(botId, helperId, workThread));

        await Until(() => Talk.WorkPane!.Messages.Should().ContainSingle());
        Talk.WorkPane!.ReadOnly.Should().BeTrue();
        Talk.WorkPane.ReadHint.Should().Be("Sample helper did this for Marshal. It is here to read.");
        Talk.WorkPane.List.Asker.Should().Be("Marshal");
        Talk.SideTitle.Should().Be("Sample helper's side");
        Talk.RailName.Should().Be("Sample helper's side of work handed to it");
        Talk.CloseRailName.Should().Be("Close Sample helper's side");
        Server.Sent("PUT", $"{Space}/threads/{workThread}/seen").Should().BeEmpty("opening it marks nothing seen");

        await Go(Addresses.Work(botId, Guid.NewGuid(), workThread));

        await Until(() => Talk.WorkNotice!.Title.Should().Be("Bot not found"));
        Talk.WorkNotice!.ActionLabel.Should().Be("Back to Marshal");
        Talk.WorkPane.Should().BeNull();
        Talk.SideTitle.Should().Be("A removed bot's side");
    }

    [Fact]
    public async Task When_the_bot_stops_working_the_summary_and_the_crew_are_read_again()
    {
        var events = RunStarts();
        await Go($"/talk/{botId}/summary");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var (summary, files) = (Talk.SummaryVersion, Talk.ConversationVersion);
        await Send(() => MainPane, "Another sample question");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());
        var activity = Talk.ActivityVersion;

        Server.Answer("GET", $"{Space}/bots", new[] { Marshal, Helper with { MadeByBotId = botId } });
        await events.Send(Event(2, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)));
        await events.End();

        await Until(() => Talk.SummaryVersion.Should().BeGreaterThan(summary, "the summary on screen is read again"));
        Talk.ConversationVersion.Should().BeGreaterThan(files);
        Talk.ActivityVersion.Should().BeGreaterThan(activity);
        await Until(() => ChatRows.CrewRows(Talk.Crew).Should().HaveCount(2, "a bot the lead made shows up without a reload"));
    }

    #endregion

    #region Side Chats

    [Fact]
    public async Task The_way_to_the_side_chats_says_how_many_there_are()
    {
        await Go($"/talk/{botId}");

        await Until(() => Talk.SideChatsLabel.Should().Be("Side chats"));
        Talk.Side.Should().Be(SideView.Closed);
    }

    [Fact]
    public async Task The_side_chats_open_beside_the_main_conversation_and_say_what_a_side_chat_is()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { Thread(null), Side });

        await Go($"/talk/{botId}/side");

        await Until(() => Talk.RailName.Should().Be("Side chats with Marshal"));
        Talk.SideTitle.Should().Be("Side chats");
        Talk.At.Should().Be("side", "the main conversation stays, and the rail has the page's heading");
        Talk.SideChatsHint.Should().Be("A side chat is a separate conversation with Marshal. What is said there stays out of the main conversation.");
        var chat = Talk.SideChats.Should().ContainSingle("the main conversation is not a side chat").Subject;
        chat.Href.Should().Be($"/talk/{botId}/side/{sideId}");
        chat.Title.Should().Be("Sample side chat");
        Addresses.NewSideChat(botId).Should().Be($"/talk/{botId}/side/new");
        Talk.CloseRailName.Should().Be("Close side chats");
        Talk.RailCloses.Should().BeTrue();
        Talk.SideChatsLabel.Should().Be("1 side chat");
        Talk.SidePane.Should().BeNull();
    }

    [Fact]
    public async Task A_side_chat_shows_its_own_messages_under_its_title_with_a_way_back_to_the_others()
    {
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { Thread(null), Side });
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side);
        Server.Answer("GET", $"{Space}/threads/{sideId}/messages", new[] { Said("user", "Sample aside") });

        await Go($"/talk/{botId}/side/{sideId}");

        await Until(() => SidePane.Messages.Single().Text.Should().Be("Sample aside"));
        Talk.SideTitle.Should().Be("Sample side chat");
        Talk.RailCloses.Should().BeFalse("its first control leads back to the list");
        Talk.AllSideChatsName.Should().Be("All side chats with Marshal");
        await Until(() => MainPane.Messages.Should().HaveCount(2, "the main conversation is still there"));
        SidePane.ReadOnly.Should().BeFalse("each conversation has its own field");
    }

    [Fact]
    public async Task A_side_chat_that_is_gone_says_so_and_leads_back_to_the_bot()
    {
        Server.Answer("GET", $"{Space}/threads/{sideId}", HttpStatusCode.NotFound);

        await Go($"/talk/{botId}/side/{sideId}");

        await Until(() => SidePane.Missing.Should().BeTrue());
        SidePane.MissingNotice.Title.Should().Be("Side chat not found");
        SidePane.MissingNotice.ActionHref.Should().Be($"/talk/{botId}");
        SidePane.MissingNotice.ActionLabel.Should().Be("Back to Marshal");
    }

    [Fact]
    public async Task A_side_chat_that_fails_to_load_can_be_tried_again_and_one_that_is_gone_still_says_so()
    {
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side);
        Server.Answer("GET", $"{Space}/threads/{sideId}/messages", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}/side/{sideId}");
        await Until(() => SidePane.LoadFailed.Should().BeTrue());
        SidePane.Composer.Disabled.Should().BeTrue();

        Server.Answer("GET", $"{Space}/threads/{sideId}/messages", new[] { Said("user", "Sample aside") with { ThreadId = sideId } });
        await Do(SidePane.Reload);

        await Until(() => SidePane.Messages.Should().ContainSingle());
        SidePane.LoadFailed.Should().BeFalse();
        SidePane.Composer.Disabled.Should().BeFalse();
        Server.Sent("GET", $"{Space}/threads/{sideId}/messages").Should().HaveCount(2);

        var gone = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2");
        Server.Answer("GET", $"{Space}/threads/{gone}", HttpStatusCode.NotFound);
        await Go($"/talk/{botId}/side/{gone}");

        await Until(() => Talk.SidePane!.Missing.Should().BeTrue());
        var gonePane = Talk.SidePane!;
        gonePane.LoadFailed.Should().BeFalse();
        gonePane.MissingNotice.Title.Should().Be("Side chat not found");
        Server.Sent("GET", $"{Space}/threads/{gone}/messages").Should().BeEmpty("a side chat that is gone is not read");
    }

    [Fact]
    public async Task A_side_chat_that_belongs_to_another_bot_is_not_shown_under_this_one()
    {
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side with { BotId = helperId });

        await Go($"/talk/{botId}/side/{sideId}");

        await Until(() => SidePane.Missing.Should().BeTrue());
        Server.Sent("GET", $"{Space}/threads/{sideId}/messages").Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_message_of_a_new_side_chat_makes_its_thread_and_moves_to_its_address()
    {
        Server.Answer("POST", $"{Space}/bots/{botId}/threads", Side with { Title = null }, HttpStatusCode.Created);
        Server.Answer("POST", $"{Space}/threads/{sideId}/messages",
            new SendMessageResponse(Said("user", "First sample words") with { ThreadId = sideId }, Run("queued") with { ThreadId = sideId }), HttpStatusCode.Accepted);
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side with { Title = "First sample words", ActiveRunId = runId });
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}/side/new");
        await Until(() => Talk.SideTitle.Should().Be("New side chat"));
        var written = SidePane;
        var focused = 0;
        written.Composer.FocusRequested += () => focused++;

        await Send(() => SidePane, "First sample words");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        await Until(() => Address.Should().Be($"talk/{botId}/side/{sideId}"));
        Navigator.CanGoBack.Should().BeTrue();
        await Until(() => SidePane.Messages.Single().Text.Should().Be("First sample words"));
        Talk.SideTitle.Should().Be("First sample words");
        SidePane.Should().BeSameAs(written, "the conversation on screen is kept, not made again");
        Server.Sent("GET", $"{Space}/threads/{sideId}/messages").Should().BeEmpty("the conversation on screen is kept, not loaded again");
        focused.Should().BeGreaterThan(0, "the cursor goes back to the message field after the address changed");
    }

    [Fact]
    public async Task The_first_message_of_a_new_side_chat_is_back_in_the_field_when_its_thread_cannot_be_made()
    {
        Server.Refuse("POST", $"{Space}/bots/{botId}/threads", HttpStatusCode.Conflict, "This bot cannot open another side chat.");
        await Go($"/talk/{botId}/side/new");
        await Until(() => Talk.SideTitle.Should().Be("New side chat"));

        await Send(() => SidePane, "First sample words");

        await Until(() => SidePane.List.Problem.Should().Be("Your message was not sent. This bot cannot open another side chat. It is back in the message field."));
        SidePane.Composer.Text.Should().Be("First sample words");
        SidePane.Composer.CanSend.Should().BeTrue();
        Server.Sent("POST", $"{Space}/threads/{sideId}/messages").Should().BeEmpty();
    }

    [Fact]
    public async Task Going_from_one_side_chat_to_another_gives_the_other_a_conversation_of_its_own()
    {
        var otherId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1");
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side);
        Server.Answer("GET", $"{Space}/threads/{sideId}/messages", new[] { Said("user", "Sample aside") });
        Server.Answer("GET", $"{Space}/threads/{otherId}", Side with { Id = otherId, Title = "Another sample side chat" });
        Server.Answer("GET", $"{Space}/threads/{otherId}/messages", new[] { Said("user", "Another sample aside") });
        await Go($"/talk/{botId}/side/{sideId}");
        await Until(() => SidePane.Messages.Single().Text.Should().Be("Sample aside"));
        var (first, main) = (SidePane, MainPane);

        await Go($"/talk/{botId}/side/{otherId}");

        await Until(() => SidePane.Messages.Single().Text.Should().Be("Another sample aside"));
        SidePane.Should().NotBeSameAs(first);
        MainPane.Should().BeSameAs(main, "the main conversation stays where it was");

        await Go($"/talk/{botId}/side");
        await Until(() => Talk.SidePane.Should().BeNull());
        MainPane.Should().BeSameAs(main);
    }

    #endregion

    #region Writing

    [Fact]
    public async Task Sending_shows_the_message_at_once_and_the_bot_thinking_with_stop_in_place_of_send()
    {
        var events = RunStarts();
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var shown = 0;
        MainPane.List.NewestRequested += () => shown++;

        await Send(() => MainPane, "Another sample question");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        await Until(() => MainPane.List.Thinking.Should().BeTrue());
        MainPane.Messages[^1].Should().Match<ChatMessage>(message => message.FromPerson && message.Text == "Another sample question");
        (MainPane.Composer.ShowsStop, MainPane.Composer.ShowsSend).Should().Be((true, false));
        MainPane.Composer.Text.Should().BeEmpty();
        MainPane.Composer.IsWorking.Should().BeTrue("the line travels around the composer");
        shown.Should().Be(1, "the person sees what they wrote, wherever they had scrolled to");
        await Until(() => ChatRows.CrewRowName(Marshal, Talk.WorkingBots, Talk.ForYou).Should().Be("Marshal, thinking"));
        Talk.MainBusy.Should().BeTrue("the head in the header is lit");
    }

    [Fact]
    public async Task The_reply_appears_as_it_streams_and_send_returns_when_the_run_ends()
    {
        var events = RunStarts();
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Send(() => MainPane, "Another sample question");

        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.StepStarted, new StepChanged(1, replyId)));
        await events.Send(Event(3, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", "Second sample")));

        await Until(() => MainPane.Messages[^1].Text.Should().Be("Second sample"));
        MainPane.Messages[^1].Streaming.Should().BeTrue();
        MainPane.List.Thinking.Should().BeFalse("the bot has started to answer");
        MainPane.Composer.ShowsStop.Should().BeTrue();

        await events.Send(Event(4, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", " answer")));
        await Until(() => MainPane.Messages[^1].Text.Should().Be("Second sample answer"));

        var reply = Said("assistant", "Second sample answer") with { Id = replyId };
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample answer"), Said("user", "Another sample question"), reply });
        await events.Send(Event(5, RunEventTypes.MessageCompleted, new MessageCompleted(reply)));
        await events.Send(Event(6, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)));
        await events.End();

        await Until(() => (MainPane.Composer.ShowsStop, MainPane.Composer.ShowsSend).Should().Be((false, true)));
        MainPane.Messages.Should().HaveCount(4, "the streamed reply and the stored one are the same message");
        MainPane.Messages[^1].Streaming.Should().BeFalse();
        Talk.MainBusy.Should().BeFalse();
    }

    [Fact]
    public async Task A_message_sent_while_the_bot_works_waits_as_pending_and_takes_its_place_when_delivered()
    {
        var events = RunStarts();
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Send(() => MainPane, "Another sample question");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());

        var steering = Said("user", "Keep it short") with { Position = null, Steers = true };
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages", new SendMessageResponse(steering, Run("running")), HttpStatusCode.Accepted);
        await Do(() =>
        {
            MainPane.Composer.Text = "Keep it short";
            MainPane.Composer.HasTwoActions.Should().BeTrue("Send comes back beside Stop once there is something written");
            return MainPane.Composer.Send();
        });

        await Until(() => MainPane.Messages[^1].Pending.Should().BeTrue());
        ChatRows.Pending("Marshal").Should().Be("Sent. Marshal reads it at its next step.");
        MainPane.Messages[^1].Text.Should().Be("Keep it short");
        MainPane.Composer.ShowsStop.Should().BeTrue("the run is still the same one");

        await events.Send(Event(2, RunEventTypes.MessageDelivered, new MessageDelivered(steering with { Position = 3 })));

        await Until(() => MainPane.Messages.Should().NotContain(message => message.Pending));
        MainPane.Messages.Where(message => message.FromPerson).Select(message => message.Text)
            .Should().Equal("Sample question", "Another sample question", "Keep it short");
    }

    [Fact]
    public async Task A_run_that_fails_ends_the_conversation_with_what_failed_and_says_it_aloud()
    {
        var events = RunStarts();
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Send(() => MainPane, "Another sample question");
        Main(Thread(null) with { Attention = new(false, true, false, Failure) });

        await events.Send(Event(1, RunEventTypes.RunFailed, new RunStatusChanged("failed", null, new("invalid_key", "The provider rejected the API key."), 1, null)));
        await events.End();

        var words = "Marshal could not reach its model. Check the model connection, then try again.";
        await Until(() => AttentionWords.Failure(MainPane.List.FailureCause!.Value, "Marshal").Should().Be(words));
        MainPane.List.Spoken.Should().Be(words);
        (MainPane.Composer.ShowsStop, MainPane.Composer.ShowsSend).Should().Be((false, true));
        MainPane.Notice.Should().BeNull("a failed run is not a notice: the conversation ends with its failure line");
        MainPane.List.Failure!.Reported.Should().Be("invalid_key: Sample report of the failure.", "the server's report is shown only when asked for");
        MainPane.List.FailureLimitsHref.Should().BeNull("only a limit stop offers a link to Limits");
    }

    [Fact]
    public async Task A_run_stopped_at_a_limit_names_the_limit_with_numbers_and_links_a_bot_editor_to_limits()
    {
        var steps = new RunFailure(
            failedRunId,
            FailureCauses.BudgetReached,
            "step_limit: This work stopped after 50 model calls.",
            new RunFailureLimit(FailureLimits.ModelCalls, 50, 50));
        Main(Thread(null) with { Attention = new(false, true, false, steps) });
        await Go($"/talk/{botId}");

        await Until(() => MainPane.List.FailureSentence.Should().Be("Marshal stopped after 50 of 50 model calls."));
        MainPane.List.Spoken.Should().Be("Marshal stopped after 50 of 50 model calls.");
        MainPane.List.FailureLimitsLabel.Should().Be("Open limits");
        MainPane.List.FailureLimitsHref.Should().Be(Addresses.BotLimits(botId));

        var tokens = new RunFailure(
            Guid.Parse("0196f1a0-0000-7000-8000-00000000000b"),
            FailureCauses.BudgetReached,
            "token_limit: This work stopped after it used 5,012,340 tokens.",
            new RunFailureLimit(FailureLimits.Tokens, 5_012_340, 5_000_000));
        var older = new RunFailure(
            Guid.Parse("0196f1a0-0000-7000-8000-00000000000c"),
            FailureCauses.BudgetReached,
            "step_limit: Older run without structured limit data.");

        var list = new MessageListViewModel { BotName = "Marshal", LimitsHref = Addresses.BotLimits(botId), Failure = tokens };
        list.Update();
        list.FailureSentence.Should().Be("Marshal stopped after 5,012,340 of 5,000,000 tokens.");
        list.FailureLimitsHref.Should().Be(Addresses.BotLimits(botId));

        list.Failure = older;
        list.Update();
        list.FailureSentence.Should().Be("Marshal stopped at a limit on how much one piece of work may use. An owner can change it in the bot's settings, under Limits.");
        list.FailureLimitsHref.Should().Be(Addresses.BotLimits(botId));
    }

    [Fact]
    public async Task A_member_who_cannot_edit_the_bot_sees_the_limit_sentence_without_open_limits()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [Person.Spaces[0] with { Role = "member" }] });
        var steps = new RunFailure(
            failedRunId,
            FailureCauses.BudgetReached,
            "step_limit: This work stopped after 50 model calls.",
            new RunFailureLimit(FailureLimits.ModelCalls, 50, 50));
        Main(Thread(null) with { Attention = new(false, true, false, steps) });

        await Go($"/talk/{botId}");

        await Until(() => MainPane.List.FailureSentence.Should().Be("Marshal stopped after 50 of 50 model calls."));
        MainPane.List.FailureLimitsHref.Should().BeNull("a member cannot edit the bot's limits");
        MainPane.List.FailureLimitsLabel.Should().BeNull();
    }

    [Fact]
    public async Task Try_again_starts_a_new_run_from_the_same_message_and_follows_it()
    {
        Main(Thread(null) with { Attention = new(false, true, false, Failure) });
        Server.Answer("POST", $"{Space}/runs/{failedRunId}/retry", Run("queued"), HttpStatusCode.Accepted);
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.List.Failure.Should().NotBeNull());
        var focused = 0;
        MainPane.Composer.FocusRequested += () => focused++;

        await Do(() => MainPane.List.OnRetry!());
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());
        MainPane.List.Failure.Should().BeNull("a run after the failure is under way");
        Server.Sent("POST", $"{Space}/runs/{failedRunId}/retry").Should().ContainSingle();

        // Focus goes to the composer, where Stop took the place of Try again.
        focused.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_retry_the_server_refuses_says_why()
    {
        Main(Thread(null) with { Attention = new(false, false, false, Failure) });
        Server.Refuse("POST", $"{Space}/runs/{failedRunId}/retry", HttpStatusCode.Conflict, "This run cannot be tried again: something came after it, or it did not fail.");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.List.Failure.Should().NotBeNull());

        await Do(() => MainPane.List.OnRetry!());

        await Until(() => MainPane.List.Problem.Should().StartWith("It was not tried again. This run cannot be tried again"));
        MainPane.List.Failure.Should().NotBeNull("the line and its button stay");
        MainPane.List.Retrying.Should().BeFalse();
    }

    [Fact]
    public async Task A_message_the_server_refuses_is_reported_and_nothing_is_added()
    {
        Server.Refuse("POST", $"{Space}/threads/{threadId}/messages", HttpStatusCode.Conflict, "The bot is still working on this thread.");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Send(() => MainPane, "Another sample question");

        await Until(() => MainPane.List.Problem.Should().Be("Your message was not sent. The bot is still working on this thread. It is back in the message field."));
        MainPane.Messages.Should().HaveCount(2);
        MainPane.Composer.Text.Should().Be("Another sample question");
    }

    [Fact]
    public async Task A_steer_the_server_refuses_puts_the_words_back_in_the_field()
    {
        var events = RunStarts();
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Send(() => MainPane, "Another sample question");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());

        Server.Refuse("POST", $"{Space}/threads/{threadId}/messages", HttpStatusCode.Conflict, "The bot is still working on this thread.");
        await Send(() => MainPane, "Keep it short");

        await Until(() => MainPane.List.Problem.Should().Be("Your message was not sent. The bot is still working on this thread. It is back in the message field."));
        MainPane.Composer.Text.Should().Be("Keep it short");
        MainPane.Messages.Should().NotContain(message => message.Pending);
    }

    [Fact]
    public async Task A_message_that_could_not_reach_the_server_is_back_in_the_field_with_its_files()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3, Preview: "one"), HttpStatusCode.Created);
        Server.Fail("POST", $"{Space}/threads/{threadId}/messages", new HttpRequestException("The server cannot be reached."));
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));
        await Until(() => Status(MainPane).Should().Equal("3 bytes"));
        await Send(() => MainPane, "Another sample question");

        await Until(() => MainPane.List.Problem.Should().Be("Your message was not sent. Drover could not reach its server. It is back in the message field."));
        MainPane.Composer.Text.Should().Be("Another sample question");
        MainPane.Composer.Attachments.Should().ContainSingle().Which.Should().Match<AttachmentItem>(item => item.Name == "notes.txt" && item.Phase == UploadPhase.Ready);
        MainPane.Composer.CanSend.Should().BeTrue("one press of Send tries the same message with the same files");
    }

    [Fact]
    public async Task A_message_that_timed_out_is_back_in_the_field()
    {
        Server.TimeOut("POST", $"{Space}/threads/{threadId}/messages");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Send(() => MainPane, "Another sample question");

        await Until(() => MainPane.List.Problem.Should().Be("Your message was not sent. Drover's server did not answer in time. It is back in the message field."));
        MainPane.Composer.Text.Should().Be("Another sample question");
        MainPane.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Stop_asks_the_server_to_cancel_the_run_and_says_it_was_stopped()
    {
        Main(Thread(runId));
        Server.Answer("POST", $"{Space}/runs/{runId}/cancel", Run("running"));
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());

        await Do(() => MainPane.Composer.Cancel());
        await Until(() => Server.Sent("POST", $"{Space}/runs/{runId}/cancel").Should().ContainSingle());
        await events.Send(Event(2, RunEventTypes.RunCancelled, new RunStatusChanged("cancelled", null, null, 1, null)));
        await events.End();

        await Until(() => MainPane.List.Notice.Should().Be("You stopped this reply."));
        (MainPane.Composer.ShowsStop, MainPane.Composer.ShowsSend).Should().Be((false, true));
    }

    [Fact]
    public async Task A_stop_the_server_does_not_hear_says_the_reply_was_not_stopped()
    {
        Main(Thread(runId));
        Server.Answer("POST", $"{Space}/runs/{runId}/cancel", HttpStatusCode.InternalServerError);
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());

        await Do(() => MainPane.Composer.Cancel());

        await Until(() => MainPane.List.Problem.Should().StartWith("The reply was not stopped. "));
        MainPane.Composer.ShowsStop.Should().BeTrue();
    }

    [Fact]
    public async Task A_reply_under_way_when_the_page_loads_is_rebuilt_from_its_events_and_goes_on()
    {
        Main(Thread(runId));
        var events = Server.Stream($"{Space}/runs/{runId}/events");

        await Go($"/talk/{botId}");
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.StepStarted, new StepChanged(1, replyId)));
        await events.Send(Event(3, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", "Said before the reload,")));
        await events.Send(Event(4, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", " and after it")));

        await Until(() => MainPane.Messages[^1].Text.Should().Be("Said before the reload, and after it"));
        MainPane.Composer.ShowsStop.Should().BeTrue();
    }

    [Fact]
    public async Task An_answer_to_a_tool_call_goes_to_the_server_and_one_answered_already_says_so()
    {
        var callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000f1");
        Server.Answer("POST", $"{Space}/tool-calls/{callId}/decision", Run("running"));
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        var answered = "not asked";
        await Do(async () => answered = await MainPane.List.Decide!(callId, true));
        answered.Should().BeNull();
        Server.Sent("POST", $"{Space}/tool-calls/{callId}/decision").Single().Should().Contain("true");

        Server.Refuse("POST", $"{Space}/tool-calls/{callId}/decision", HttpStatusCode.Conflict, "Sample refusal.");
        await Do(async () => answered = await MainPane.List.Decide!(callId, false));
        answered.Should().Be("This request was answered already.");
    }

    [Fact]
    public async Task Forgetting_a_note_from_its_card_marks_the_card_and_undo_brings_the_note_back()
    {
        var noteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000f2");
        Server.Answer("POST", $"{Space}/bots/{botId}/memory/notes/{noteId}/forget", new RemovedNoteResponse(noteId, "sample-undo", DateTimeOffset.MaxValue));
        Server.Answer("POST", $"{Space}/bots/{botId}/memory/notes/restore", new MemoryNoteResponse(noteId, "Sample note", "bot", DateTimeOffset.UnixEpoch, null));
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        ForgetOutcome? outcome = null;
        await Do(async () => outcome = await MainPane.List.Forget!(noteId, "Sample note"));

        outcome.Should().Be(new ForgetOutcome(true));
        MainPane.List.ForgottenNotes.Should().Contain(noteId);
        var toast = Toasts.Toasts.Should().ContainSingle().Subject;
        toast.Message.Should().Be(MemoryWords.Removed("Sample note"));
        toast.Action!.Label.Should().Be("Undo");

        await Do(() => toast.Action.Run());

        await Until(() => MainPane.List.ForgottenNotes.Should().BeEmpty("the card says so no longer once undone"));
    }

    [Fact]
    public async Task A_note_that_changed_since_its_card_was_drawn_is_not_forgotten_and_one_already_gone_counts_as_forgotten()
    {
        var noteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000f3");
        Server.Refuse("POST", $"{Space}/bots/{botId}/memory/notes/{noteId}/forget", HttpStatusCode.Conflict, "Sample refusal.");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        ForgetOutcome? outcome = null;
        await Do(async () => outcome = await MainPane.List.Forget!(noteId, "Sample note"));
        outcome.Should().Be(new ForgetOutcome(false, Changed: true));

        Server.Answer("POST", $"{Space}/bots/{botId}/memory/notes/{noteId}/forget", HttpStatusCode.NotFound);
        await Do(async () => outcome = await MainPane.List.Forget!(noteId, "Sample note"));
        outcome.Should().Be(new ForgetOutcome(true));
        Toasts.Toasts.Should().BeEmpty("there is nothing to undo");
    }

    #endregion

    #region Files

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_file_attached_is_uploaded_at_once_and_goes_with_the_message(bool sizeKnown)
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3, Preview: "one"), HttpStatusCode.Created);
        RunStarts(string.Empty);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one", sizeKnown));

        await Until(() => Status(MainPane).Should().Equal("3 bytes"));
        Server.Addresses.Should().Contain($"POST {Space}/threads/{threadId}/files?name=notes.txt");
        Server.Sent("POST", $"{Space}/threads/{threadId}/files").Should().Equal("one");
        await Do(() => MainPane.Composer.Send());

        await Until(() => Server.Sent("POST", $"{Space}/threads/{threadId}/messages").Should().ContainSingle()
            .Which.Should().Contain($"\"fileIds\":[\"{fileId}\"]"));
        await Until(() => MainPane.Composer.Attachments.Should().BeEmpty("the files went with the message"));
    }

    [Fact]
    public async Task What_another_app_shared_becomes_the_chosen_bots_draft_and_nothing_is_sent()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3, Preview: "one"), HttpStatusCode.Created);
        Inbox.Offer(new SharedDraft("Sample words from another app", [new SamplePickedFile("notes.txt", "one")]));
        Inbox.SendTo(botId);

        await Go($"/talk/{botId}");

        await Until(() => Status(MainPane).Should().Equal("3 bytes"));
        MainPane.Composer.Text.Should().Be("Sample words from another app");
        Server.Sent("POST", $"{Space}/threads/{threadId}/messages").Should().BeEmpty("sending is the person's own act");
        Inbox.Waiting.Should().BeNull();
        Server.Sent("PUT", $"{Space}/bots/{botId}/main-thread").Should().ContainSingle("the share waited for the conversation to load");
    }

    [Fact]
    public async Task A_share_meant_for_another_bot_stays_out_of_this_conversation()
    {
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        Inbox.SendTo(Guid.NewGuid());

        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        MainPane.Composer.Text.Should().BeEmpty();
        Inbox.Waiting.Should().NotBeNull();
    }

    [Fact]
    public async Task A_file_the_server_refuses_says_why_before_anything_is_sent_and_holds_the_message()
    {
        AttachmentsAllowed();
        Server.Answer(
            "POST",
            $"{Space}/threads/{threadId}/files",
            new { title = "Marshal has no way to open this kind of file. It needs a sandbox for it.", status = 422, refusal = FileRefusals.NothingCanOpenIt },
            HttpStatusCode.UnprocessableEntity);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("tool.bin", [0, 1, 2]));

        await Until(() => Status(MainPane).Should().Equal("Marshal has no way to open this kind of file. It needs a sandbox for it."));
        ChatRows.HasProblem(MainPane.Composer.Attachments.Single()).Should().BeTrue();
        await Do(() =>
        {
            MainPane.Composer.Text = "Here it is";
            return Task.CompletedTask;
        });
        MainPane.Composer.CanSend.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_refused_because_the_conversation_is_private_says_so_in_the_composer()
    {
        AttachmentsAllowed();
        Server.Answer(
            "POST",
            $"{Space}/threads/{threadId}/files",
            new { title = "This conversation is private, and others in this space can reach Marshal's sandbox, so this file cannot go there.", status = 422, refusal = FileRefusals.PrivateConversation },
            HttpStatusCode.UnprocessableEntity);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("report.pdf", [0, 1, 2]));

        await Until(() => Status(MainPane).Should().Equal(
            "This conversation is private, and others in this space can reach Marshal's sandbox, so this file cannot go there."));
        ChatRows.Announced(MainPane.Composer.Attachments).Should().Be("1 file cannot be sent.");
        MainPane.Composer.Attachments.Single().Name.Should().Be("report.pdf");
        MainPane.Composer.CanSend.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_file_past_the_servers_limit_is_refused_without_being_sent(bool sizeKnown)
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/attachment-rules", new AttachmentRules(2, 10, false, false));
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("big.txt", "three", sizeKnown));

        await Until(() => Status(MainPane).Should().Equal("Larger than this server takes."));
        Server.Sent("POST", $"{Space}/threads/{threadId}/files").Should().BeEmpty();
    }

    [Fact]
    public async Task A_pasted_picture_is_uploaded_like_a_chosen_file()
    {
        var fileId = Guid.CreateVersion7();
        var name = FileWords.PastedPictureName(new(2026, 11, 20, 14, 3, 0, TimeSpan.Zero));
        AttachmentsAllowed();
        Server.Answer(
            "POST",
            $"{Space}/threads/{threadId}/files",
            new FileResponse(fileId, name, "image/png", FileKinds.Image, 3, 1, 1),
            HttpStatusCode.Created);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile(name, [1, 2, 3]));

        await Until(() => Status(MainPane).Should().Equal("3 bytes"));
        MainPane.Composer.Attachments.Should().ContainSingle()
            .Which.Name.Should().Be("Pasted image 2026-11-20 14.03.png");
        Server.Addresses.Should()
            .Contain($"POST {Space}/threads/{threadId}/files?name=Pasted%20image%202026-11-20%2014.03.png");
    }

    [Fact]
    public async Task Files_are_taken_once_the_conversation_is_read_and_not_while_it_is_being_read()
    {
        var messages = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        await Go($"/talk/{botId}");
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().ContainSingle());
        MainPane.AcceptsFiles.Should().BeFalse("it is still being read");
        MainPane.Composer.TakesFiles.Should().BeFalse("Attach is not offered while nothing would take the files");

        messages.SetResult();

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        MainPane.AcceptsFiles.Should().BeTrue();
        MainPane.Composer.TakesFiles.Should().BeTrue();
    }

    [Fact]
    public async Task A_side_chat_that_is_not_there_takes_no_files()
    {
        Server.Answer("GET", $"{Space}/threads/{sideId}", HttpStatusCode.NotFound);

        await Go($"/talk/{botId}/side/{sideId}");

        await Until(() => SidePane.Missing.Should().BeTrue());
        SidePane.AcceptsFiles.Should().BeFalse();
    }

    [Fact]
    public async Task A_bots_side_of_handed_work_is_only_read_so_it_takes_no_files()
    {
        var workThread = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");
        BotSummary[] crew = [Marshal, Helper];
        ThreadSummary[] helpersThreads = [];
        MessageResponse[] said = [Said("user", "Sample task") with { ThreadId = workThread }];
        Server.Answer("GET", $"{Space}/bots", crew);
        Server.Answer("GET", $"{Space}/bots/{helperId}/threads", helpersThreads);
        Server.Answer(
            "GET",
            $"{Space}/threads/{workThread}",
            new ThreadSummary(workThread, helperId, null, false, DateTimeOffset.UnixEpoch, null));
        Server.Answer("GET", $"{Space}/threads/{workThread}/messages", said);

        await Go(Addresses.Work(botId, helperId, workThread));

        await Until(() => Talk.WorkPane!.Messages.Should().ContainSingle());
        Talk.WorkPane!.AcceptsFiles.Should().BeFalse();
    }

    [Fact]
    public async Task Files_handed_to_a_conversation_that_does_not_take_them_are_not_uploaded()
    {
        var messages = Server.Hold("GET", $"{Space}/threads/{threadId}/messages");
        AttachmentsAllowed();
        await Go($"/talk/{botId}");
        await Until(() => Server.Sent("GET", $"{Space}/threads/{threadId}/messages").Should().ContainSingle());

        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));

        MainPane.Composer.Attachments.Should()
            .BeEmpty("a conversation still being read takes no files, however they arrived");
        Server.Sent("POST", $"{Space}/threads/{threadId}/files").Should().BeEmpty();
        messages.SetResult();
        await Until(() => MainPane.Messages.Should().HaveCount(2));
    }

    [Fact]
    public async Task A_conversation_that_could_not_be_read_takes_no_files_even_when_they_are_handed_over()
    {
        Server.Fail("GET", $"{Space}/threads/{threadId}/messages", new HttpRequestException("connection refused"));
        AttachmentsAllowed();

        await Go("/");
        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));

        MainPane.Composer.Attachments.Should().BeEmpty();
        Server.Sent("POST", $"{Space}/threads/{threadId}/files").Should().BeEmpty();
    }

    [Fact]
    public async Task A_conversation_that_could_not_be_read_takes_no_files()
    {
        Server.Fail("GET", $"{Space}/threads/{threadId}/messages", new HttpRequestException("connection refused"));

        await Go("/");

        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        MainPane.AcceptsFiles.Should().BeFalse();
        MainPane.Composer.TakesFiles.Should().BeFalse("Attach is not offered when the files would go nowhere");
    }

    [Fact]
    public async Task A_share_waits_while_the_conversation_could_not_be_read()
    {
        Server.Fail("GET", $"{Space}/threads/{threadId}/messages", new HttpRequestException("connection refused"));
        AttachmentsAllowed();
        Inbox.Offer(new SharedDraft("Sample words from another app", [new SamplePickedFile("notes.txt", "one")]));
        Inbox.SendTo(botId);

        await Go($"/talk/{botId}");
        await Until(() => MainPane.LoadFailed.Should().BeTrue());
        await Do(() => Task.CompletedTask);

        Inbox.Waiting.Should().NotBeNull("the share is kept for when the conversation can be read");
        MainPane.Composer.TakesFiles.Should().BeFalse();
        MainPane.Composer.Text.Should().BeEmpty();
        MainPane.Composer.Attachments.Should().BeEmpty();
        Server.Sent("POST", $"{Space}/threads/{threadId}/files").Should().BeEmpty();
    }

    [Fact]
    public async Task Files_handed_to_a_conversation_that_was_left_make_no_thread_and_ask_nothing()
    {
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/bots/{botId}/threads", Side with { Title = null }, HttpStatusCode.Created);
        await Go($"/talk/{botId}/side/new");
        await Until(() => Talk.SideTitle.Should().Be("New side chat"));
        var pane = SidePane;
        var file = new SamplePickedFile("notes.txt", "one");

        await Do(() =>
        {
            Talk.Dispose();
            return Task.CompletedTask;
        });
        await Attach(() => pane, file);

        pane.AcceptsFiles.Should().BeFalse("a conversation that was left takes nothing");
        Server.Sent("POST", $"{Space}/bots/{botId}/threads").Should().BeEmpty();
        Server.Addresses.Should().NotContain(address => address.Contains("attachment-rules"));
        file.Opened.Should().Be(0);
    }

    [Fact]
    public async Task A_file_handed_over_as_the_conversation_is_left_is_never_opened()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/attachment-rules", new AttachmentRules(1024, 10, true, true));
        var rules = Server.Hold("GET", $"{Space}/threads/{threadId}/attachment-rules");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        var pane = MainPane;
        var file = new SamplePickedFile("notes.txt", "one");

        var attached = Attach(() => pane, file);
        await Until(() => Server.Addresses.Should().Contain($"GET {Space}/threads/{threadId}/attachment-rules"));
        await Do(() =>
        {
            Talk.Dispose();
            return Task.CompletedTask;
        });
        rules.SetResult();
        await attached;

        file.Opened.Should().Be(0, "the conversation was left before the rules came back, so nothing is added to it");
        Server.Sent("POST", $"{Space}/threads/{threadId}/files").Should().BeEmpty();
    }

    [Fact]
    public async Task A_notice_from_the_composer_reaches_the_person()
    {
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        MainPane.Composer.Notify("What was pasted could not be attached.", ToastKind.Error);

        Toasts.Toasts.Should().ContainSingle().Which.Should().Match<ToastMessage>(toast =>
            toast.Kind == ToastKind.Error && toast.Message == "What was pasted could not be attached.");
    }
    [Fact]
    public async Task Removing_an_uploaded_file_removes_it_from_the_server_too()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("DELETE", $"{Space}/files/{fileId}", HttpStatusCode.NoContent);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));
        await Until(() => Status(MainPane).Should().Equal("3 bytes"));

        await Do(() => MainPane.Composer.Remove(MainPane.Composer.Attachments.Single().Id));

        await Until(() => Server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{Space}/files/{fileId}"));
        MainPane.Composer.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_file_of_a_new_side_chat_makes_its_thread_once_even_when_a_message_races_it()
    {
        var fileId = Guid.CreateVersion7();
        var making = Server.Hold("POST", $"{Space}/bots/{botId}/threads");
        Server.Answer("POST", $"{Space}/bots/{botId}/threads", Side with { Title = null }, HttpStatusCode.Created);
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side with { Title = null });
        Server.Answer("GET", $"{Space}/threads/{sideId}/attachment-rules", new AttachmentRules(1024, 10, true, true));
        Server.Answer("POST", $"{Space}/threads/{sideId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("POST", $"{Space}/threads/{sideId}/messages",
            new SendMessageResponse(Said("user", "First sample words") with { ThreadId = sideId }, Run("queued") with { ThreadId = sideId }), HttpStatusCode.Accepted);
        Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}/side/new");
        await Until(() => Talk.SideTitle.Should().Be("New side chat"));

        // The message asks for the thread first, and the file while it is still on its way: both wait on the one request.
        var submitted = Send(() => SidePane, "First sample words");
        var attached = Attach(() => SidePane, new SamplePickedFile("notes.txt", "one"));
        await Until(() => Server.Requests.Should().Contain(request => request.Method == "POST" && request.Path == $"{Space}/bots/{botId}/threads"));
        making.SetResult();
        await submitted;
        await attached;

        await Until(() => Address.Should().Be($"talk/{botId}/side/{sideId}", string.Join("\n", Server.Addresses)));
        await Until(() => Status(SidePane).Should().Equal("3 bytes"));
        Server.Sent("POST", $"{Space}/bots/{botId}/threads").Should().ContainSingle("the message and the file share one new thread");
        Server.Sent("POST", $"{Space}/threads/{sideId}/messages").Should().ContainSingle();
        Server.Sent("POST", $"{Space}/threads/{sideId}/files").Should().ContainSingle("the file went to the thread the message went to");
    }

    [Fact]
    public async Task A_transport_failure_records_its_cause_without_the_file_name_or_exception_messages()
    {
        var name = $"private-{Guid.NewGuid():N}.png";
        var message = $"private-{Guid.NewGuid():N}";
        var cause = new IOException(name);
        var failure = new HttpRequestException(message, new InvalidOperationException(message, cause));
        AttachmentsAllowed();
        Server.Fail("POST", $"{Space}/threads/{threadId}/files", failure);
        var log = new DiagnosticLog(new MemoryDiagnosticStore(), TimeProvider.System);
        HashSet<string> unhandled = [];
        using var factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new DiagnosticLogProvider(log, TimeProvider.System, unhandled)));
        using var talk = new TalkViewModel(Server.CreateApi(), Session, Navigator, Toasts, Inbox, Ui, factory);
        await Do(() => talk.Open(new TalkAddress(botId)));
        await Until(() => talk.MainPane!.Messages.Should().HaveCount(2));

        await Attach(() => talk.MainPane!, new SamplePickedFile(name, "one", sizeKnown: false));
        await Until(() => talk.MainPane!.Composer.Attachments.Single().Phase.Should().Be(UploadPhase.Failed));

        var saved = log.Compose(new AppDescription("test", "test"));
        saved.Should().Contain("ExceptionType=HttpRequestException CauseType=IOException");
        saved.Should().NotContain(name).And.NotContain(message);
    }

    [Fact]
    public async Task A_size_refusal_without_a_problem_document_explains_the_failed_upload()
    {
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", HttpStatusCode.RequestEntityTooLarge);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("screen.png", "one", sizeKnown: false));

        await Until(() => Status(MainPane).Should().Equal("Larger than this server takes."));
        MainPane.Composer.CanSend.Should().BeFalse();
    }

    [Fact]
    public async Task An_upload_that_fails_with_no_reason_from_the_server_says_so_and_holds_the_message()
    {
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", HttpStatusCode.InternalServerError);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));
        await Do(() =>
        {
            MainPane.Composer.Text = "Here";
            return Task.CompletedTask;
        });

        await Until(() => Status(MainPane).Should().Equal("Not uploaded. Remove it and attach it again."));
        MainPane.Composer.CanSend.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_taken_off_while_it_uploads_is_removed_from_the_server_when_it_arrives()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        var uploading = Server.Hold("POST", $"{Space}/threads/{threadId}/files");
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("DELETE", $"{Space}/files/{fileId}", HttpStatusCode.NoContent);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));
        await Until(() => Status(MainPane).Single().Should().StartWith("Uploading"));

        await Do(() => MainPane.Composer.Remove(MainPane.Composer.Attachments.Single().Id));
        uploading.SetResult();

        await Until(() => Server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{Space}/files/{fileId}"));
        MainPane.Composer.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_attached_while_a_message_is_on_its_way_stays_for_the_next_one()
    {
        var sentId = Guid.CreateVersion7();
        var laterId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(sentId, "first.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        var sending = Server.Hold("POST", $"{Space}/threads/{threadId}/messages");
        RunStarts("With a file");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Attach(() => MainPane, new SamplePickedFile("first.txt", "one"));
        await Until(() => Status(MainPane).Should().Equal("3 bytes"));

        var submitted = Send(() => MainPane, "With a file");
        await Until(() => MainPane.Sending.Should().BeTrue());
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(laterId, "later.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        await Attach(() => MainPane, new SamplePickedFile("later.txt", "two"));
        await Until(() => MainPane.Composer.Attachments.Should().HaveCount(2));
        await Until(() => Status(MainPane).Should().Equal("3 bytes", "3 bytes"));
        sending.SetResult();
        await submitted;

        await Until(() => MainPane.Composer.Attachments.Select(item => item.Name).Should().Equal("later.txt"));
        Server.Sent("POST", $"{Space}/threads/{threadId}/messages").Single().Should().Contain(sentId.ToString()).And.NotContain(laterId.ToString());
    }

    [Fact]
    public async Task Leaving_a_conversation_removes_the_files_attached_and_not_sent()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{Space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("DELETE", $"{Space}/files/{fileId}", HttpStatusCode.NoContent);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        await Attach(() => MainPane, new SamplePickedFile("notes.txt", "one"));
        await Until(() => Status(MainPane).Should().Equal("3 bytes"));

        await Do(() =>
        {
            Talk.Dispose();
            return Task.CompletedTask;
        });

        await Until(() => Server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{Space}/files/{fileId}"));
    }

    [Fact]
    public async Task A_link_to_a_moment_lands_on_its_message()
    {
        var question = Said("user", "Sample question");
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question, Said("assistant", "Sample answer") });

        await Go($"/talk/{botId}#message-{question.Id}");

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Talk.Moment.Should().Be(question.Id);
        await Until(() => MainPane.List.Messages.Where(MainPane.List.IsMoment).Should().ContainSingle("only the message the link names takes focus")
            .Which.Key.Should().Be(question.Id.ToString()));
        MessageListViewModel.Anchor(question.Id).Should().Be($"message-{question.Id}");
        (await Ui.Read(MainPane.List.TakeMoment)).Should().Be(question.Id);
        (await Ui.Read(MainPane.List.TakeMoment)).Should().BeNull("a moment is shown once");

        await Go($"/talk/{botId}");
        await Until(() => MainPane.List.Moment.Should().BeNull());
        (await Ui.Read(MainPane.List.TakeMoment)).Should().BeNull();
        await Go($"/talk/{botId}#message-{question.Id}");
        await Until(() => MainPane.List.Moment.Should().Be(question.Id));
        (await Ui.Read(MainPane.List.TakeMoment)).Should().Be(question.Id, "a link followed again lands again");
    }

    [Fact]
    public async Task A_moment_in_the_main_conversation_is_not_followed_while_something_is_open_beside_it()
    {
        var question = Said("user", "Sample question");
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question });
        Server.Answer("GET", $"{Space}/threads/{sideId}", Side);
        Server.Answer("GET", $"{Space}/threads/{sideId}/messages", new[] { question with { ThreadId = sideId } });

        await Go($"/talk/{botId}/side/{sideId}#message-{question.Id}");

        await Until(() => SidePane.List.Moment.Should().Be(question.Id, "the link names a message of the side chat"));
        MainPane.List.Moment.Should().BeNull();
    }

    #endregion

    #region Choosing The Model

    [Fact]
    public async Task The_conversation_offers_the_bots_models_and_a_choice_goes_to_the_server()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/models", Models(null));
        Server.Answer("PUT", $"{Space}/threads/{threadId}/model", Models("sample-stronger"));

        await Go($"/talk/{botId}");
        await Until(() => MainPane.Composer.HasModelChoice.Should().BeTrue());
        MainPane.Composer.ModelOptions.Select(option => option.Label).Should().Equal("Sample default (the bot's own)", "Sample stronger");

        await Do(() => MainPane.Composer.ChooseModel("sample-stronger"));

        await Until(() => MainPane.Composer.ChosenModel.Should().Be("sample-stronger"));
        Server.Sent("PUT", $"{Space}/threads/{threadId}/model").Single().Should().Contain("sample-stronger");
    }

    [Fact]
    public async Task A_choice_the_server_refuses_is_said_and_the_choice_goes_back()
    {
        const string Refusal = "Sample stronger could cost more in one run than this space lets a run cost (10.00 USD).";
        Server.Answer("GET", $"{Space}/threads/{threadId}/models", Models(null));
        Server.Answer("PUT", $"{Space}/threads/{threadId}/model", new { title = Refusal }, HttpStatusCode.BadRequest);

        await Go($"/talk/{botId}");
        await Until(() => MainPane.Composer.HasModelChoice.Should().BeTrue());
        await Do(() => MainPane.Composer.ChooseModel("sample-stronger"));

        await Until(() => Toasts.Toasts.Should().ContainSingle(toast => toast.Message == Refusal && toast.Kind == ToastKind.Error));
        await Until(() => MainPane.Composer.ChosenModel.Should().Be(ComposerViewModel.BotsOwnModel));
    }

    [Fact]
    public async Task Without_the_models_the_conversation_reads_as_before_and_offers_no_choice()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/models", HttpStatusCode.InternalServerError);

        await Go($"/talk/{botId}");

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        MainPane.Composer.HasModelChoice.Should().BeFalse();
        MainPane.Problem.Should().BeNull("the choice is an option; failing to read it is not the conversation's failure");
    }

    [Fact]
    public async Task Each_stored_reply_carries_the_model_that_wrote_it()
    {
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[]
        {
            Said("user", "Sample question"),
            Said("assistant", "Sample answer") with { RunId = runId, Model = "sample-stronger" },
        });

        await Go($"/talk/{botId}");

        await Until(() => MainPane.Messages.Should().HaveCount(2));
        MainPane.Messages.Select(message => message.Model).Should().Equal(null, "sample-stronger");
    }

    #endregion

    #region Versions

    [Fact]
    public async Task Write_another_reply_asks_the_server_follows_the_new_run_and_reads_the_conversation_again()
    {
        var question = Said("user", "Sample question");
        var reply = Said("assistant", "Sample answer") with { Id = replyId, RunId = failedRunId };
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question, reply });
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages/{replyId}/regenerate", new SendMessageResponse(question, Run("queued")), HttpStatusCode.Accepted);
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        MainPane.Messages.Select(message => (message.MayEdit, message.MayRegenerate)).Should().Equal((true, false), (false, true));
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question });

        await Do(() => MainPane.List.Regenerate(replyId));
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());
        Server.Sent("POST", $"{Space}/threads/{threadId}/messages/{replyId}/regenerate").Should().ContainSingle();
        MainPane.Messages.Select(message => message.Text).Should().Equal(["Sample question"], "the earlier reply is kept on the server, not shown");
        MainPane.Messages.Should().OnlyContain(message => !message.MayEdit && !message.MayRegenerate, "nothing new is offered while the bot works");
    }

    [Fact]
    public async Task Retrying_an_unusable_tool_request_reply_uses_the_existing_regenerate_flow()
    {
        const string RawEnvelope =
            "<\uFF5CDSML\uFF5Ccalls>\n<\uFF5CDSML\uFF5Cinvoke name=\"sandbox.run\">\n"
            + "<\uFF5CDSML\uFF5Cparameter name=\"command\" string=\"true\">ls -la</\uFF5CDSML\uFF5Cparameter>\n"
            + "</\uFF5CDSML\uFF5Cinvoke>\n</\uFF5CDSML\uFF5Ccalls>";
        var question = Said("user", "List the files");
        var reply = Said("assistant", RawEnvelope) with { Id = replyId, RunId = failedRunId };
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question, reply });
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages/{replyId}/regenerate", new SendMessageResponse(question, Run("queued")), HttpStatusCode.Accepted);
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        var unusable = MainPane.Messages[1];
        unusable.UnusableToolRequest.Should().BeTrue();
        unusable.Tools.Should().BeEmpty();
        ChatRows.OffersUnusableRetry(unusable, canAsk: true).Should().BeTrue();
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question });

        await Do(() => MainPane.List.Regenerate(replyId));
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());
        Server.Sent("POST", $"{Space}/threads/{threadId}/messages/{replyId}/regenerate").Should().ContainSingle();
        MainPane.Messages.Should().OnlyContain(message => !message.UnusableToolRequest && !message.MayRegenerate);
    }

    [Fact]
    public async Task An_edit_goes_to_the_server_with_its_words_and_the_new_run_is_followed()
    {
        var question = Said("user", "Sample question");
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question, Said("assistant", "Sample answer") });
        var edited = Said("user", "Sample question, edited");
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages/{question.Id}/edit", new SendMessageResponse(edited, Run("queued")), HttpStatusCode.Accepted);
        var events = Server.Stream($"{Space}/runs/{runId}/events");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { edited with { Versions = [question.Id, edited.Id] } });

        await Do(() => MainPane.List.Edit(question.Id, "  Sample question, edited  "));
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        await Until(() => MainPane.Composer.ShowsStop.Should().BeTrue());
        Server.Sent("POST", $"{Space}/threads/{threadId}/messages/{question.Id}/edit").Single().Should().Contain("\"Sample question, edited\"", "the edit is sent trimmed");
        MainPane.Messages.Single().Versions.Should().Be(new VersionSwitch(2, 2, question.Id, null, Enabled: false));
    }

    [Fact]
    public async Task Showing_another_version_asks_the_server_and_shows_the_conversation_it_now_holds()
    {
        var question = Said("user", "Sample question");
        var second = Said("assistant", "Second sample answer") with { Id = replyId };
        var first = Said("assistant", "First sample answer");
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question, second with { Versions = [first.Id, replyId] } });
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages/{first.Id}/show", HttpStatusCode.NoContent);
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));
        MainPane.Messages[1].Versions.Should().Be(new VersionSwitch(2, 2, first.Id, null));
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { question, first with { Versions = [first.Id, replyId] } });

        await Do(() => MainPane.List.ShowVersion(first.Id));

        await Until(() => MainPane.Messages[1].Text.Should().Be("First sample answer"));
        MainPane.Messages[1].Versions.Should().Be(new VersionSwitch(1, 2, null, replyId));
        Server.Sent("POST", $"{Space}/threads/{threadId}/messages/{first.Id}/show").Should().ContainSingle();
    }

    [Fact]
    public async Task Another_reply_the_server_refuses_says_why_and_the_conversation_stays()
    {
        var reply = Said("assistant", "Sample answer") with { Id = replyId };
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), reply });
        Server.Refuse("POST", $"{Space}/threads/{threadId}/messages/{replyId}/regenerate", HttpStatusCode.Conflict, "The bot is still working in this conversation. Wait for it to finish, or stop it.");
        await Go($"/talk/{botId}");
        await Until(() => MainPane.Messages.Should().HaveCount(2));

        await Do(() => MainPane.List.Regenerate(replyId));

        await Until(() => MainPane.List.Problem.Should().StartWith("No other reply was asked for. The bot is still working"));
        MainPane.Messages.Should().HaveCount(2);
        MainPane.Messages[1].MayRegenerate.Should().BeTrue("the person may ask again once the bot is done");
    }

    #endregion

    #region Private Methods

    private static ConversationModelChoice Models(string? chosen) =>
        new("sample-default", "Sample default", chosen, [new("sample-default", "Sample default"), new("sample-stronger", "Sample stronger")]);

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, botId, null, false, DateTimeOffset.UnixEpoch, activeRunId, IsMain: true);

    // The main conversation is opened through its bot and read again by its own address when a run ends.
    private void Main(ThreadSummary thread)
    {
        Server.Answer("PUT", $"{Space}/bots/{botId}/main-thread", thread);
        Server.Answer("GET", $"{Space}/threads/{threadId}", thread);
    }

    private static MessageResponse Said(string role, string text) =>
        new(Guid.CreateVersion7(), threadId, 0, role, null, [new("text") { Text = text }], DateTimeOffset.UnixEpoch);

    private static RunSummary Run(string status) =>
        new(runId, threadId, botId, runId, status, null, null, 1, 0, new(0, 0, 0, 0), null, null);

    private static RunEventResponse Event(long seq, string type, object payload) =>
        new(seq, runId, type, RunEventTypes.Write(payload), DateTimeOffset.UnixEpoch);

    // The server accepts the next message and starts a run whose events the test then writes.
    private LiveEvents RunStarts(string text = "Another sample question")
    {
        Server.Answer("POST", $"{Space}/threads/{threadId}/messages", new SendMessageResponse(Said("user", text), Run("queued")), HttpStatusCode.Accepted);
        return Server.Stream($"{Space}/runs/{runId}/events");
    }

    private LiveEvents ChangeElsewhere(params MessageResponse[] messages)
    {
        var changed = Thread(runId) with { LastActivityAt = DateTimeOffset.UnixEpoch.AddMinutes(1) };
        Main(changed);
        ThreadSummary[] threads = [changed];
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", threads);
        Server.Answer("GET", $"{Space}/threads/{threadId}/messages", messages);
        return Server.Stream($"{Space}/runs/{runId}/events");
    }

    private bool RunWatched() =>
        Server.Addresses.Any(address =>
            address.StartsWith($"GET {Space}/runs/{runId}/events", StringComparison.Ordinal));

    private void AttachmentsAllowed() =>
        Server.Answer("GET", $"{Space}/threads/{threadId}/attachment-rules", new AttachmentRules(1024, 10, true, true));

    private Task Attach(Func<ConversationViewModel> pane, IPickedFile file) => Do(() => pane().Composer.Chosen([file]));

    // What each chip of the tray says under its file's name.
    private static IEnumerable<string> Status(ConversationViewModel pane) =>
        pane.Composer.Attachments.Select(item => FileWords.Status(item, pane.Bot.Name));

    // Each bot's row in the crew, as it is read out.
    private IEnumerable<string> CrewNames() =>
        ChatRows.CrewRows(Talk.Crew).Select(row => ChatRows.CrewRowName(row.Bot, Talk.WorkingBots, Talk.ForYou));

    // The word beside each bot that has something for the person.
    private IEnumerable<string> CrewWords() =>
        ChatRows.CrewRows(Talk.Crew).Select(row => AttentionWords.Word(Talk.ForYou.GetValueOrDefault(row.Bot.Id))).OfType<string>();

    // The request for the person at the bot's screen that waits in the main conversation.
    private Takeover? Waiting() =>
        MainPane.Messages.AllTools().Select(tool => tool.Takeover).LastOrDefault(takeover => takeover is { Waits: true });

    // A stored call asking for a person at the screen, with no result yet: the request still waits.
    private static MessageResponse Handover() =>
        new(Guid.CreateVersion7(), threadId, 1, "assistant", runId,
            [new("tool_call") { ToolCallId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c8"), ToolName = "computer_handover", Arguments = """{"reason":"Sign in."}""" }],
            DateTimeOffset.UnixEpoch);

    private static BotSandboxStatus ScreenStatus(bool desktop) =>
        new(Available: true, HasSandbox: true, "running", DateTimeOffset.UnixEpoch, Desktop: desktop, BotWorking: false, DesktopOffered: true);

    #endregion
}
