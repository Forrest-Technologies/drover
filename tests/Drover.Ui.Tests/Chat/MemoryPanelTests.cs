namespace Drover.Ui.Tests.Chat;

/// <summary>
/// What a bot remembers about the person reading, as a panel: every state with its sentence and next step, a note
/// changed in place and named for what it says, and where the focus goes as a note is changed, saved, and removed.
/// The first of these came with the panel's starter.
/// </summary>
public sealed class MemoryPanelTests : BunitContext
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Constructors

    public MemoryPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region From The Starter

    [Fact]
    public void A_note_is_changed_in_place_and_named_for_what_it_says()
    {
        (Guid Id, string Text)? changed = null;
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, "/moment");
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Notes, [note])
            .Add(view => view.OnChange, (id, text) =>
            {
                changed = (id, text);
                return Task.FromResult<string?>(null);
            }));

        panel.Find("ul").GetAttribute("aria-label").Should().Be("What Sample bot remembers about you");
        panel.Find(".dv-memory-origin").TextContent.Should().Contain("Sample bot noted this").And.Contain("Open that moment");
        panel.Find("[aria-label='Change the note: Prefers short answers.']").Click();
        panel.Find(".dv-memory-edit textarea").Input("Prefers short answers, with sources.");
        panel.Find(".dv-memory-edit").Submit();

        changed.Should().Be((note.Id, "Prefers short answers, with sources."));
        panel.FindAll(".dv-memory-edit").Should().BeEmpty();
    }

    [Fact]
    public void A_note_that_was_not_saved_keeps_its_text_and_says_why()
    {
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.OnAdd, _ => Task.FromResult<string?>("The server did not answer. Try again.")));

        panel.Find(".dv-memory-add textarea").Input("Works in Berlin time.");
        panel.Find(".dv-memory-add").Submit();

        panel.Find(".dv-memory-add textarea").GetAttribute("value").Should().Be("Works in Berlin time.");
        panel.Find("[role=alert]").TextContent.Should().Contain("The server did not answer. Try again.");
    }

    [Theory]
    [InlineData(MemoryPanelState.Loading, "Loading what Sample bot remembers.")]
    [InlineData(MemoryPanelState.Off, "Memory is off for Sample bot, so it keeps no notes.")]
    [InlineData(MemoryPanelState.Ready, "Sample bot has not noted anything about you yet.")]
    public void The_memory_panel_says_what_is_happening_in_every_state(MemoryPanelState state, string sentence)
    {
        var panel = Render<MemoryPanel>(set => set.Add(view => view.State, state).Add(view => view.BotName, "Sample bot"));

        panel.Find(".dv-memory-hint").TextContent.Should().StartWith(sentence);
    }

    #endregion

    #region States And Their Next Step

    [Fact]
    public void Memory_that_is_off_links_an_owner_to_the_setting_and_tells_anyone_else_only_that_it_is_off()
    {
        var forOwner = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Off)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.EditorHref, "/bots/sample?section=memory"));
        var forMember = Render<MemoryPanel>(set => set.Add(view => view.State, MemoryPanelState.Off).Add(view => view.BotName, "Sample bot"));

        var link = forOwner.Find("a");
        link.TextContent.Should().Be("Turn memory on in Sample bot's settings");
        link.GetAttribute("href").Should().Be("/bots/sample?section=memory");
        forMember.FindAll("a").Should().BeEmpty();
        forMember.FindAll("textarea").Should().BeEmpty("nothing can be written while memory is off");
    }

    [Fact]
    public void An_empty_list_says_when_the_bot_keeps_a_note_and_offers_adding_one()
    {
        var panel = Render<MemoryPanel>(set => set.Add(view => view.State, MemoryPanelState.Ready).Add(view => view.BotName, "Sample bot"));

        panel.Find(".dv-memory-hint").TextContent.Should().Be(
            "Sample bot has not noted anything about you yet. It keeps a note when it learns a preference or a fact worth keeping.");
        panel.Find(".dv-memory-add label").TextContent.Should().Be("Add a note");
        panel.Find(".dv-memory-add button").TextContent.Trim().Should().Be("Add the note");
    }

    [Fact]
    public void A_list_that_failed_to_load_says_why_and_tries_again()
    {
        var retried = false;
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Failed)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Problem, "The server failed to answer.")
            .Add(view => view.OnRetry, () => retried = true));

        panel.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the notes");
        panel.Find(".dv-empty button").Click();

        retried.Should().BeTrue();
    }

    [Fact]
    public void Nearly_full_notes_say_so_in_a_sentence_and_otherwise_nothing_is_said_about_their_size()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", false, start, null);
        var full = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready).Add(view => view.BotName, "Sample bot").Add(view => view.Notes, [note]).Add(view => view.NearlyFull, true));
        var roomy = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready).Add(view => view.BotName, "Sample bot").Add(view => view.Notes, [note]));

        full.Markup.Should().Contain("Sample bot's notes are nearly full. From here it merges or replaces notes when it learns something new.");
        roomy.Markup.Should().NotContain("nearly full");
        full.Find(".dv-memory-origin").TextContent.Should().StartWith("You wrote this");
        full.FindAll(".dv-memory-origin a").Should().BeEmpty("a note a person wrote has no moment to open");
    }

    #endregion

    #region Focus

    [Fact]
    public void Changing_a_note_moves_the_focus_into_its_field_and_saving_moves_it_back_to_the_note()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, "/moment");
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Notes, [note])
            .Add(view => view.OnChange, (_, _) => Task.FromResult<string?>(null)));

        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus");
        panel.Find("[aria-label='Change the note: Prefers short answers.']").Click();
        var intoField = Focused(JSInterop.VerifyFocusAsyncInvoke());
        panel.Find(".dv-memory-edit").Submit();
        var focused = JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);

        panel.FindAll(".dv-memory-edit").Should().BeEmpty();
        Focused(focused[1]).Should().NotBe(intoField, "the focus leaves the field, which is gone, for the note's Change button");
    }

    [Fact]
    public void Cancelling_a_change_keeps_the_note_as_it_was_and_returns_the_focus_to_it()
    {
        var note = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Notes, [note]));

        panel.Find("[aria-label='Change the note: Prefers short answers.']").Click();
        panel.Find(".dv-memory-edit textarea").Input("Something else.");
        panel.FindAll(".dv-memory-edit button").Single(button => button.TextContent.Trim() == "Cancel").Click();
        var focused = JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2);

        panel.Find(".dv-memory-text").TextContent.Should().Be("Prefers short answers.");
        Focused(focused[1]).Should().NotBe(Focused(focused[0]));
    }

    [Fact]
    public void Removing_a_note_hands_it_over_and_keeps_the_focus_in_the_list_once_it_is_gone()
    {
        Guid? removed = null;
        var first = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var second = new MemoryNote(Guid.NewGuid(), "Works in Berlin time.", false, start, null);
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Notes, [first, second])
            .Add(view => view.OnRemove, id => removed = id));

        panel.Find("[aria-label='Remove the note: Prefers short answers.']").Click();
        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus", "nothing moves while the note is still there");
        panel.Render(set => set.Add(view => view.Notes, [second]));

        removed.Should().Be(first.Id);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void Removing_the_last_note_moves_the_focus_to_adding_one()
    {
        var only = new MemoryNote(Guid.NewGuid(), "Prefers short answers.", true, start, null);
        var panel = Render<MemoryPanel>(set => set
            .Add(view => view.State, MemoryPanelState.Ready)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Notes, [only])
            .Add(view => view.OnRemove, _ => { }));

        panel.Find("[aria-label='Remove the note: Prefers short answers.']").Click();
        panel.Render(set => set.Add(view => view.Notes, []));

        JSInterop.VerifyFocusAsyncInvoke();
        panel.Find(".dv-memory-hint").TextContent.Should().StartWith("Sample bot has not noted anything about you yet.");
    }

    #endregion

    #region Private Methods

    private static string Focused(JSRuntimeInvocation invocation) => ((ElementReference)invocation.Arguments[0]!).Id;

    #endregion
}
