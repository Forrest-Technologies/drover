namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// What a bot remembers, beside its conversation, against the fake server: the notes with where each came from,
/// writing through the server, and removal with Undo in a toast. The cases are the web client's for the rail; the ones
/// about the page around it, and about "Forget" on a card in the conversation, belong to the conversation's own tests.
/// </summary>
public sealed class MemoryRailTests : IDisposable
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid sideId = Guid.Parse("0196f1a0-0000-7000-8000-000000000009");

    private static readonly Guid messageId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1");

    private static readonly Guid noteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");

    private static readonly Guid sideNoteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d2");

    private static readonly Guid ownNoteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d3");

    private static readonly string memory = $"{ChatBench.Space}/bots/{botId}/memory";

    private readonly FakeTimeProvider time = new();

    private readonly ChatBench bench;

    #endregion

    #region Constructors

    public MemoryRailTests()
    {
        bench = new(time: time);
    }

    #endregion

    #region Properties

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Sample instructions", Guid.Empty, "sample-default", "active");

    private static BotMemoryResponse Notes { get; } = new(true, false,
    [
        new(noteId, "Prefers tea.", "bot", DateTimeOffset.UnixEpoch, new(threadId, messageId, true)),
        new(sideNoteId, "Starts work at nine.", "bot", DateTimeOffset.UnixEpoch, new(sideId, messageId, false)),
        new(ownNoteId, "Works in Berlin time.", "person", DateTimeOffset.UnixEpoch, null),
    ]);

    #endregion

    #region Public Methods

    public void Dispose() => bench.Dispose();

    #endregion

    #region The Rail

    [Fact]
    public async Task Memory_lists_every_note_with_where_it_came_from()
    {
        bench.Server.Answer("GET", memory, Notes);
        var (rail, panel) = await Open();

        rail.State.Should().Be(MemoryPanelState.Ready);
        panel.Notes.Should().HaveCount(3);
        panel.ListName.Should().Be("What Marshal remembers about you");
        panel.Origin(panel.Notes[0]).Should().StartWith("Marshal noted this");
        panel.Notes[0].OriginHref.Should().Be($"/talk/{botId}#message-{messageId}");
        panel.Notes[1].OriginHref.Should().Be($"/talk/{botId}/side/{sideId}#message-{messageId}");
        panel.Origin(panel.Notes[2]).Should().StartWith("You wrote this");
        panel.Notes[2].OriginHref.Should().BeNull();
    }

    [Fact]
    public async Task Memory_that_is_off_says_so_and_sends_an_owner_to_the_bots_memory_settings()
    {
        bench.Server.Answer("GET", memory, new BotMemoryResponse(false, false, []));
        var (rail, panel) = await Open();

        rail.State.Should().Be(MemoryPanelState.Off);
        panel.OffHint.Should().Be("Memory is off for Marshal, so it keeps no notes.");
        panel.EditorHref.Should().Be($"/bots/{botId}?section=memory");
    }

    [Fact]
    public async Task Someone_who_may_not_change_the_bot_is_not_sent_to_its_settings()
    {
        using var member = new ChatBench("member");
        member.Server.Answer("GET", memory, new BotMemoryResponse(false, false, []));
        var rail = member.Make<MemoryRailViewModel>();
        rail.Bot = Marshal;

        await rail.ParametersSet();

        rail.EditorHref.Should().BeNull();
    }

    [Fact]
    public async Task Notes_that_fail_to_load_say_why_and_load_on_a_second_try()
    {
        bench.Server.Answer("GET", memory, HttpStatusCode.InternalServerError);
        var (rail, panel) = await Open();

        rail.State.Should().Be(MemoryPanelState.Failed);
        MemoryPanelViewModel.FailedTitle.Should().Be("Drover could not load the notes");
        panel.FailedCause.Should().NotBeNullOrWhiteSpace();

        bench.Server.Answer("GET", memory, Notes);
        panel.TryAgainNow();

        await ChatBench.Eventually(() => panel.Notes.Should().HaveCount(3));
        rail.State.Should().Be(MemoryPanelState.Ready);
    }

    [Fact]
    public void Loading_says_what_is_loading()
    {
        bench.Server.Hold("GET", memory);
        var rail = bench.Make<MemoryRailViewModel>();
        rail.Bot = Marshal;
        var panel = Wire(rail);

        _ = rail.ParametersSet();

        rail.State.Should().Be(MemoryPanelState.Loading);
        panel.LoadingHint.Should().Be("Loading what Marshal remembers.");
    }

    [Fact]
    public async Task Notes_read_again_that_fail_stay_on_screen_and_a_toast_says_once_that_they_may_be_old()
    {
        bench.Server.Answer("GET", memory, Notes);
        var (rail, panel) = await Open();
        bench.Server.Answer("GET", memory, HttpStatusCode.InternalServerError);

        rail.Version = 1;
        await rail.ParametersSet();
        rail.Version = 2;
        await rail.ParametersSet();

        rail.State.Should().Be(MemoryPanelState.Ready);
        panel.Notes.Should().HaveCount(3);
        bench.Toasts.Toasts.Should().ContainSingle("a server that stays away does not stack them")
            .Which.Message.Should().StartWith("The notes could not be read again, so they may be out of date.");
    }

    #endregion

    #region Writing

    [Fact]
    public async Task A_note_added_and_a_note_changed_go_to_the_server_and_the_list_is_read_again()
    {
        bench.Server.Answer("GET", memory, new BotMemoryResponse(true, false, []));
        bench.Server.Answer("POST", $"{memory}/notes", new MemoryNoteResponse(ownNoteId, "Works in Berlin time.", "person", DateTimeOffset.UnixEpoch, null), HttpStatusCode.Created);
        var (_, panel) = await Open();
        bench.Server.Answer("GET", memory, new BotMemoryResponse(true, false, [Notes.Notes[2]]));

        panel.Adding = "Works in Berlin time.";
        await panel.Add();

        panel.Notes.Should().ContainSingle().Which.Text.Should().Be("Works in Berlin time.");
        bench.Server.Answer("PUT", $"{memory}/notes/{ownNoteId}", new MemoryNoteResponse(ownNoteId, "Works in Oslo time.", "person", DateTimeOffset.UnixEpoch, null));
        bench.Server.Answer("GET", memory, new BotMemoryResponse(true, false, [Notes.Notes[2] with { Text = "Works in Oslo time." }]));
        var note = panel.Notes.Single();
        MemoryPanelViewModel.ChangeName(note).Should().Be("Change the note: Works in Berlin time.");
        panel.Edit(note);
        panel.Draft = "Works in Oslo time.";
        await panel.Save(note);

        panel.Notes.Single().Text.Should().Be("Works in Oslo time.");
        bench.Server.Sent("POST", $"{memory}/notes").Should().ContainSingle().Which.Should().Contain("Works in Berlin time.");
        bench.Server.Sent("PUT", $"{memory}/notes/{ownNoteId}").Should().ContainSingle().Which.Should().Contain("Works in Oslo time.");
    }

    [Fact]
    public async Task A_refused_note_keeps_its_text_and_says_the_servers_reason()
    {
        bench.Server.Answer("GET", memory, new BotMemoryResponse(true, true, []));
        bench.Server.Refuse("POST", $"{memory}/notes", HttpStatusCode.BadRequest,
            "The notes hold 95 of the 100 characters they may hold, and this note needs 21. Shorten or remove a note first.");
        var (_, panel) = await Open();

        panel.Adding = "Works in Berlin time.";
        await panel.Add();

        panel.SaveProblem.Should().Contain("Shorten or remove a note first.");
        panel.Adding.Should().Be("Works in Berlin time.");
        panel.NearlyFullNote.Should().StartWith("Marshal's notes are nearly full.");
    }

    [Fact]
    public async Task A_removed_note_leaves_at_once_and_the_toasts_Undo_puts_it_back()
    {
        bench.Server.Answer("GET", memory, Notes);
        bench.Server.Answer("DELETE", $"{memory}/notes/{noteId}", Receipt(noteId));
        bench.Server.Answer("POST", $"{memory}/notes/restore", Notes.Notes[0]);
        var (_, panel) = await Open();
        using var toasts = new ToastHostViewModel(bench.Toasts);

        panel.RemoveNote(panel.Notes[0]);
        await ChatBench.Eventually(() => panel.Notes.Should().HaveCount(2));

        var toast = toasts.Quiet.Should().ContainSingle().Subject;
        toast.Message.Should().Contain("Removed the note: Prefers tea.");
        (toast.Action!.Label, toast.Action.Name).Should().Be(("Undo", "Undo removing the note: Prefers tea."));
        panel.TakeFocus().Should().Be(new MemoryFocus(Note: sideNoteId), "the focus stays in the list once the note is gone");
        await toasts.Act(toast);

        panel.Notes.Should().HaveCount(3);
        bench.Server.Sent("POST", $"{memory}/notes/restore").Should().ContainSingle().Which.Should().Contain("sealed-sample-receipt");
        bench.Toasts.Toasts.Should().BeEmpty("choosing Undo also dismisses the toast");
        panel.TakeFocus().Should().Be(new MemoryFocus(Note: noteId), "the focus moves to the note Undo put back");
    }

    [Fact]
    public async Task A_note_removed_elsewhere_first_simply_leaves_the_list()
    {
        bench.Server.Answer("GET", memory, Notes);
        bench.Server.Answer("DELETE", $"{memory}/notes/{noteId}", HttpStatusCode.NotFound);
        var (_, panel) = await Open();

        panel.RemoveNote(panel.Notes[0]);

        await ChatBench.Eventually(() => panel.Notes.Should().HaveCount(2));
        bench.Toasts.Toasts.Should().BeEmpty("the note is gone, as asked, and there is nothing to undo");
    }

    [Fact]
    public async Task The_toast_stops_offering_Undo_when_the_receipt_runs_out()
    {
        bench.Server.Answer("GET", memory, Notes);
        bench.Server.Answer("DELETE", $"{memory}/notes/{noteId}", Receipt(noteId));
        var (_, panel) = await Open();

        panel.RemoveNote(panel.Notes[0]);
        await ChatBench.Eventually(() => bench.Toasts.Toasts.Should().ContainSingle().Which.Action.Should().NotBeNull());

        time.Advance(TimeSpan.FromMinutes(9));
        bench.Toasts.Toasts.Single().Action.Should().NotBeNull("the receipt still holds");
        time.Advance(TimeSpan.FromMinutes(1));
        await ChatBench.Eventually(() => bench.Toasts.Toasts.Should().ContainSingle().Which.Action.Should().BeNull());
        bench.Toasts.Toasts.Single().Message.Should().Contain("Removed the note: Prefers tea.");
    }

    [Fact]
    public async Task Memory_that_is_off_still_lists_the_notes_it_kept_so_they_can_be_removed()
    {
        bench.Server.Answer("GET", memory, Notes with { Enabled = false });
        var (rail, panel) = await Open();

        rail.State.Should().Be(MemoryPanelState.Off);
        panel.Notes.Should().HaveCount(3);
        panel.OffHint.Should().Be("Memory is off for Marshal. It keeps these notes until you remove them, and adds no new ones.");
    }

    [Fact]
    public async Task Undo_that_comes_too_late_says_so_in_a_toast()
    {
        bench.Server.Answer("GET", memory, Notes);
        bench.Server.Answer("DELETE", $"{memory}/notes/{noteId}", Receipt(noteId));
        bench.Server.Refuse("POST", $"{memory}/notes/restore", HttpStatusCode.BadRequest,
            "This note can no longer be put back. Undo lasts ten minutes; write the note again to keep it.");
        var (_, panel) = await Open();
        using var toasts = new ToastHostViewModel(bench.Toasts);

        panel.RemoveNote(panel.Notes[0]);
        await ChatBench.Eventually(() => toasts.Quiet.Should().ContainSingle().Which.Action.Should().NotBeNull());
        await toasts.Act(toasts.Quiet.Single());

        toasts.Errors.Should().ContainSingle().Which.Message.Should().Contain("This note can no longer be put back.");
    }

    #endregion

    #region Private Methods

    private RemovedNoteResponse Receipt(Guid id) => new(id, "sealed-sample-receipt", time.GetUtcNow().AddMinutes(10));

    private async Task<(MemoryRailViewModel Rail, MemoryPanelViewModel Panel)> Open()
    {
        var rail = bench.Make<MemoryRailViewModel>();
        rail.Bot = Marshal;
        var panel = Wire(rail);
        await rail.ParametersSet();
        return (rail, panel);
    }

    // The rail fills the panel and takes its steps, as the native rail control wires the two.
    private static MemoryPanelViewModel Wire(MemoryRailViewModel rail)
    {
        var panel = new MemoryPanelViewModel { BotName = rail.Bot.Name, OnAdd = rail.Add, OnChange = rail.Change };
        panel.Remove += id => _ = rail.Remove(id);
        panel.Retry += () => _ = rail.Load();
        rail.Restored += panel.FocusNote;
        rail.PropertyChanged += (_, _) => Fill();
        Fill();
        return panel;

        void Fill()
        {
            (panel.State, panel.Notes, panel.NearlyFull, panel.Problem, panel.EditorHref) = (rail.State, rail.Notes, rail.NearlyFull, rail.Problem, rail.EditorHref);
            panel.ParametersSet();
        }
    }

    #endregion
}
