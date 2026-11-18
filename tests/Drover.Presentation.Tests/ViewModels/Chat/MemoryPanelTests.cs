namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// What a bot remembers about the person reading, as a panel: every state with its sentence and next step, a note
/// changed in place and named for what it says, and where the focus goes as a note is changed, saved, and removed.
/// The cases are the web client's, asked of the panel's view model.
/// </summary>
public sealed class MemoryPanelTests
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region From The Starter

    [Fact]
    public async Task A_note_is_changed_in_place_and_named_for_what_it_says()
    {
        (Guid Id, string Text)? changed = null;
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, "/moment");
        var panel = Panel(MemoryPanelState.Ready, note);
        panel.OnChange = (id, text) =>
        {
            changed = (id, text);
            return Task.FromResult<string?>(null);
        };

        panel.ListName.Should().Be("What Sample bot remembers about you");
        panel.Origin(note).Should().StartWith("Sample bot noted this");
        MemoryPanelViewModel.OpenMoment.Should().Be("Open that moment");
        MemoryPanelViewModel.ChangeName(note).Should().Be("Change the note: Prefers short answers.");
        panel.Edit(note);
        panel.Editing.Should().Be(note.Id);
        panel.Draft.Should().Be("Prefers short answers.");
        panel.Draft = "Prefers short answers, with sources.";
        await panel.Save(note);

        changed.Should().Be((note.Id, "Prefers short answers, with sources."));
        panel.Editing.Should().BeNull();
    }

    [Fact]
    public async Task A_note_that_was_not_saved_keeps_its_text_and_says_why()
    {
        var panel = Panel(MemoryPanelState.Ready);
        panel.OnAdd = _ => Task.FromResult<string?>("The server did not answer. Try again.");

        panel.Adding = "Works in Berlin time.";
        await panel.Add();

        panel.Adding.Should().Be("Works in Berlin time.");
        panel.SaveProblem.Should().Be("The server did not answer. Try again.");
    }

    [Fact]
    public async Task A_note_that_was_saved_empties_the_field_and_blank_text_is_never_sent()
    {
        List<string> added = [];
        var panel = Panel(MemoryPanelState.Ready);
        panel.OnAdd = text =>
        {
            added.Add(text);
            return Task.FromResult<string?>(null);
        };

        panel.Adding = "   ";
        await panel.Add();
        panel.Adding = "  Works in Berlin time.  ";
        await panel.Add();

        added.Should().Equal("Works in Berlin time.");
        panel.Adding.Should().BeEmpty("the field empties only once the server has the note");
    }

    [Fact]
    public void The_memory_panel_says_what_is_happening_in_every_state()
    {
        var panel = Panel(MemoryPanelState.Loading);

        panel.LoadingHint.Should().Be("Loading what Sample bot remembers.");
        panel.OffHint.Should().StartWith("Memory is off for Sample bot, so it keeps no notes.");
        panel.EmptyHint.Should().StartWith("Sample bot has not noted anything about you yet.");
    }

    #endregion

    #region States And Their Next Step

    [Fact]
    public void Memory_that_is_off_links_an_owner_to_the_setting_and_tells_anyone_else_only_that_it_is_off()
    {
        var forOwner = Panel(MemoryPanelState.Off);
        forOwner.EditorHref = "/bots/sample?section=memory";
        var forMember = Panel(MemoryPanelState.Off);

        forOwner.TurnOnText.Should().Be("Turn memory on in Sample bot's settings");
        forOwner.EditorHref.Should().Be("/bots/sample?section=memory");
        forMember.EditorHref.Should().BeNull();
    }

    [Fact]
    public void Memory_that_is_off_lists_the_notes_it_kept_with_remove_and_nothing_else()
    {
        Guid? removed = null;
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, "/moment");
        var panel = Panel(MemoryPanelState.Off, note);
        panel.Remove += id => removed = id;

        panel.OffHint.Should().Be("Memory is off for Sample bot. It keeps these notes until you remove them, and adds no new ones.");
        MemoryPanelViewModel.RemoveName(note).Should().Be("Remove the note: Prefers short answers.");
        panel.RemoveNote(note);

        removed.Should().Be(note.Id);
    }

    [Fact]
    public void Each_link_to_a_moment_is_named_for_its_note()
    {
        MemoryNote[] notes =
        [
            new(Guid.NewGuid(), "Prefers tea.", true, start, "/one"),
            new(Guid.NewGuid(), "Starts at nine.", true, start, "/two"),
        ];
        var panel = Panel(MemoryPanelState.Ready, notes);

        notes.Select(panel.OpenMomentName).Should().Equal(
            "Open that moment: Sample bot noted Prefers tea.", "Open that moment: Sample bot noted Starts at nine.");
    }

    [Fact]
    public void An_empty_list_says_when_the_bot_keeps_a_note_and_offers_adding_one()
    {
        var panel = Panel(MemoryPanelState.Ready);

        panel.EmptyHint.Should().Be(
            "Sample bot has not noted anything about you yet. It keeps a note when it learns a preference or a fact worth keeping.");
        MemoryPanelViewModel.AddLabel.Should().Be("Add a note");
        MemoryPanelViewModel.AddAction.Should().Be("Add the note");
        panel.AddHint.Should().Be("Something Sample bot should know about you from now on.");
    }

    [Fact]
    public void A_list_that_failed_to_load_says_why_and_tries_again()
    {
        var retried = false;
        var panel = Panel(MemoryPanelState.Failed);
        panel.Problem = "The server failed to answer.";
        panel.Retry += () => retried = true;

        MemoryPanelViewModel.FailedTitle.Should().Be("Drover could not load the notes");
        panel.FailedCause.Should().Be("The server failed to answer.");
        panel.TryAgainNow();

        retried.Should().BeTrue();
    }

    [Fact]
    public void Nearly_full_notes_say_so_in_a_sentence_and_otherwise_nothing_is_said_about_their_size()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", false, start, null);
        var full = Panel(MemoryPanelState.Ready, note);
        full.NearlyFull = true;
        var roomy = Panel(MemoryPanelState.Ready, note);

        full.NearlyFullNote.Should().Be("Sample bot's notes are nearly full. From here it merges or replaces notes when it learns something new.");
        roomy.NearlyFullNote.Should().BeNull();
        full.Origin(note).Should().StartWith("You wrote this");
        note.OriginHref.Should().BeNull("a note a person wrote has no moment to open");
    }

    #endregion

    #region Focus

    [Fact]
    public async Task Changing_a_note_moves_the_focus_into_its_field_and_saving_moves_it_back_to_the_note()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, "/moment");
        var panel = Panel(MemoryPanelState.Ready, note);
        panel.OnChange = (_, _) => Task.FromResult<string?>(null);

        panel.TakeFocus().Should().BeNull("nothing moves until the person does something");
        panel.Edit(note);
        panel.TakeFocus().Should().Be(new MemoryFocus(EditField: true));
        await panel.Save(note);

        panel.Editing.Should().BeNull();
        panel.TakeFocus().Should().Be(new MemoryFocus(Note: note.Id), "the focus leaves the field, which is gone, for the note's Change button");
        panel.TakeFocus().Should().BeNull("once");
    }

    [Fact]
    public void Cancelling_a_change_keeps_the_note_as_it_was_and_returns_the_focus_to_it()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var panel = Panel(MemoryPanelState.Ready, note);

        panel.Edit(note);
        panel.TakeFocus();
        panel.Draft = "Something else.";
        panel.Cancel(note);

        panel.Editing.Should().BeNull();
        panel.Notes.Single().Text.Should().Be("Prefers short answers.");
        panel.TakeFocus().Should().Be(new MemoryFocus(Note: note.Id));
    }

    [Fact]
    public void Removing_a_note_hands_it_over_and_keeps_the_focus_in_the_list_once_it_is_gone()
    {
        Guid? removed = null;
        var first = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var second = new MemoryNote(Guid.NewGuid(), "Works in Berlin time.", false, start, null);
        var panel = Panel(MemoryPanelState.Ready, first, second);
        panel.Remove += id => removed = id;

        panel.RemoveNote(first);
        panel.ParametersSet();
        panel.TakeFocus().Should().BeNull("nothing moves while the note is still there");
        panel.Notes = [second];
        panel.ParametersSet();

        removed.Should().Be(first.Id);
        panel.TakeFocus().Should().Be(new MemoryFocus(Note: second.Id));
    }

    [Fact]
    public void Removing_the_last_note_moves_the_focus_to_adding_one()
    {
        var only = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var panel = Panel(MemoryPanelState.Ready, only);

        panel.RemoveNote(only);
        panel.Notes = [];
        panel.ParametersSet();

        panel.TakeFocus().Should().Be(new MemoryFocus(AddField: true));
        panel.EmptyHint.Should().StartWith("Sample bot has not noted anything about you yet.");
    }

    [Fact]
    public void A_note_put_back_takes_the_focus()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var panel = Panel(MemoryPanelState.Ready, note);
        var redraws = 0;
        panel.PropertyChanged += (_, _) => redraws++;

        panel.FocusNote(note.Id);

        redraws.Should().Be(1, "the panel is drawn again so the focus can move");
        panel.TakeFocus().Should().Be(new MemoryFocus(Note: note.Id));
    }

    #endregion

    #region Private Methods

    private static MemoryPanelViewModel Panel(MemoryPanelState state, params MemoryNote[] notes)
    {
        var panel = new MemoryPanelViewModel { State = state, BotName = "Sample bot", Notes = notes };
        panel.ParametersSet();
        return panel;
    }

    #endregion
}
