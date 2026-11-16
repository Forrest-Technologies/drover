namespace Drover.Ui.Tests.Screens;

/// <summary>
/// What a bot remembers, beside its conversation, against the fake server: the rail and its address, the notes with
/// where each came from, writing through the server, removal with Undo in a toast, a link that opens the
/// conversation at its message, and "Forget" on a card.
/// </summary>
public sealed class MemoryRailTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid sideId = Guid.Parse("0196f1a0-0000-7000-8000-000000000009");

    private static readonly Guid messageId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1");

    private static readonly Guid noteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");

    private static readonly Guid sideNoteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d2");

    private static readonly Guid ownNoteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d3");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private static readonly string memory = $"{space}/bots/{botId}/memory";

    #endregion

    #region Constructors

    public MemoryRailTests()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", new[] { new ModelConnectionSummary(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null) });
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Main });
        Server.Answer("PUT", $"{space}/bots/{botId}/main-thread", Main);
        Server.Answer("GET", $"{space}/threads/{threadId}", Main);
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new[]
        {
            new MessageResponse(Guid.CreateVersion7(), threadId, 0, "user", null, [new("text") { Text = "I drink tea." }], DateTimeOffset.UnixEpoch),
            new MessageResponse(messageId, threadId, 1, "assistant", null, [new("text") { Text = "Noted." }], DateTimeOffset.UnixEpoch),
        });
    }

    #endregion

    #region Properties

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active");

    private static ThreadSummary Main { get; } = new(threadId, botId, null, false, DateTimeOffset.UnixEpoch, null, IsMain: true);

    private static BotMemoryResponse Notes { get; } = new(true, false,
    [
        new(noteId, "Prefers tea.", "bot", DateTimeOffset.UnixEpoch, new(threadId, messageId, true)),
        new(sideNoteId, "Starts work at nine.", "bot", DateTimeOffset.UnixEpoch, new(sideId, messageId, false)),
        new(ownNoteId, "Works in Berlin time.", "person", DateTimeOffset.UnixEpoch, null),
    ]);

    #endregion

    #region The Rail

    [Fact]
    public void Memory_opens_beside_the_conversation_by_its_own_address_and_lists_every_note_with_where_it_came_from()
    {
        Server.Answer("GET", memory, Notes);

        var page = RenderMemory();

        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(3));
        page.Find("aside").GetAttribute("aria-label").Should().Be("What Marshal remembers about you");
        page.Find("aside h1").TextContent.Should().Be("Memory");
        var close = page.Find("aside a.dv-shell-up");
        close.GetAttribute("aria-label").Should().Be("Close memory");
        close.GetAttribute("href").Should().Be($"/talk/{botId}");
        var way = page.Find("a.dv-shell-side[href$=\"/memory\"]");
        way.TextContent.Should().Be("Memory");
        way.GetAttribute("aria-current").Should().Be("true");
        var origins = page.FindAll("aside .dv-memory-origin");
        origins[0].TextContent.Should().StartWith("Marshal noted this");
        origins[0].QuerySelector("a")!.GetAttribute("href").Should().Be($"/talk/{botId}?message={messageId}");
        origins[1].QuerySelector("a")!.GetAttribute("href").Should().Be($"/talk/{botId}/side/{sideId}?message={messageId}");
        origins[2].TextContent.Should().StartWith("You wrote this");
        origins[2].QuerySelector("a").Should().BeNull();
        page.FindAll("main article").Should().HaveCount(2, "the main conversation stays beside it");
    }

    [Fact]
    public void Memory_that_is_off_says_so_and_sends_an_owner_to_the_bots_memory_settings()
    {
        Server.Answer("GET", memory, new BotMemoryResponse(false, false, []));

        var page = RenderMemory();

        page.WaitForAssertion(() => page.Find("aside .dv-memory-hint").TextContent.Should().Be("Memory is off for Marshal, so it keeps no notes."));
        page.Find("aside .dv-memory-link").GetAttribute("href").Should().Be($"/bots/{botId}?section=memory");
    }

    [Fact]
    public void Notes_that_fail_to_load_say_why_and_load_on_a_second_try()
    {
        Server.Answer("GET", memory, HttpStatusCode.InternalServerError);
        var page = RenderMemory();
        page.WaitForAssertion(() => page.Find("aside .dv-empty-title").TextContent.Should().Be("Drover could not load the notes"));

        Server.Answer("GET", memory, Notes);
        page.Find("aside .dv-empty button").Click();

        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(3));
    }

    [Fact]
    public void Loading_says_what_is_loading()
    {
        Server.Hold("GET", memory);

        var page = RenderMemory();

        page.WaitForAssertion(() => page.Find("aside [role=status]").TextContent.Should().Be("Loading what Marshal remembers."));
    }

    #endregion

    #region Writing

    [Fact]
    public void A_note_added_and_a_note_changed_go_to_the_server_and_the_list_is_read_again()
    {
        Server.Answer("GET", memory, new BotMemoryResponse(true, false, []));
        Server.Answer("POST", $"{memory}/notes", new MemoryNoteResponse(ownNoteId, "Works in Berlin time.", "person", DateTimeOffset.UnixEpoch, null), HttpStatusCode.Created);
        var page = RenderMemory();
        page.WaitForElement("aside .dv-memory-add");

        Server.Answer("GET", memory, new BotMemoryResponse(true, false, [Notes.Notes[2]]));
        page.Find("aside .dv-memory-add textarea").Input("Works in Berlin time.");
        page.Find("aside .dv-memory-add").Submit();
        page.WaitForAssertion(() => page.Find("aside .dv-memory-text").TextContent.Should().Be("Works in Berlin time."));

        Server.Answer("PUT", $"{memory}/notes/{ownNoteId}", new MemoryNoteResponse(ownNoteId, "Works in Oslo time.", "person", DateTimeOffset.UnixEpoch, null));
        Server.Answer("GET", memory, new BotMemoryResponse(true, false, [Notes.Notes[2] with { Text = "Works in Oslo time." }]));
        page.Find("[aria-label='Change the note: Works in Berlin time.']").Click();
        page.Find("aside .dv-memory-edit textarea").Input("Works in Oslo time.");
        page.Find("aside .dv-memory-edit").Submit();

        page.WaitForAssertion(() => page.Find("aside .dv-memory-text").TextContent.Should().Be("Works in Oslo time."));
        Server.Sent("POST", $"{memory}/notes").Should().ContainSingle().Which.Should().Contain("Works in Berlin time.");
        Server.Sent("PUT", $"{memory}/notes/{ownNoteId}").Should().ContainSingle().Which.Should().Contain("Works in Oslo time.");
    }

    [Fact]
    public void A_refused_note_keeps_its_text_and_says_the_servers_reason()
    {
        Server.Answer("GET", memory, new BotMemoryResponse(true, true, []));
        Server.Refuse("POST", $"{memory}/notes", HttpStatusCode.BadRequest,
            "The notes hold 95 of the 100 characters they may hold, and this note needs 21. Shorten or remove a note first.");
        var page = RenderMemory();
        page.WaitForElement("aside .dv-memory-add");

        page.Find("aside .dv-memory-add textarea").Input("Works in Berlin time.");
        page.Find("aside .dv-memory-add").Submit();

        page.WaitForAssertion(() => page.Find("aside [role=alert]").TextContent.Should().Contain("Shorten or remove a note first."));
        page.Find("aside .dv-memory-add textarea").GetAttribute("value").Should().Be("Works in Berlin time.");
        page.Markup.Should().Contain("Marshal's notes are nearly full.");
    }

    [Fact]
    public void A_removed_note_leaves_at_once_and_the_toasts_Undo_puts_it_back()
    {
        Server.Answer("GET", memory, Notes);
        Server.Answer("DELETE", $"{memory}/notes/{noteId}", new RemovedNoteResponse(noteId, "sealed-sample-token"));
        Server.Answer("POST", $"{memory}/notes/restore", Notes.Notes[0]);
        var toasts = Render<ToastHost>();
        var page = RenderMemory();
        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(3));

        page.Find("[aria-label='Remove the note: Prefers tea.']").Click();

        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(2));
        toasts.WaitForAssertion(() => toasts.Find(".dv-toast-message").TextContent.Should().Contain("Removed the note: Prefers tea."));
        var undo = toasts.FindAll(".dv-toast button").Single(button => button.TextContent.Trim() == "Undo");
        undo.Click();

        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(3));
        Server.Sent("POST", $"{memory}/notes/restore").Should().ContainSingle().Which.Should().Contain("sealed-sample-token");
        toasts.FindAll(".dv-toast").Should().BeEmpty("choosing Undo also dismisses the toast");
        page.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_note_removed_elsewhere_first_simply_leaves_the_list()
    {
        Server.Answer("GET", memory, Notes);
        Server.Answer("DELETE", $"{memory}/notes/{noteId}", HttpStatusCode.NotFound);
        var toasts = Render<ToastHost>();
        var page = RenderMemory();
        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(3));

        page.Find("[aria-label='Remove the note: Prefers tea.']").Click();

        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(2));
        toasts.FindAll(".dv-toast").Should().BeEmpty("the note is gone, as asked, and there is nothing to undo");
    }

    [Fact]
    public void Undo_that_comes_too_late_says_so_in_a_toast()
    {
        Server.Answer("GET", memory, Notes);
        Server.Answer("DELETE", $"{memory}/notes/{noteId}", new RemovedNoteResponse(noteId, "sealed-sample-token"));
        Server.Refuse("POST", $"{memory}/notes/restore", HttpStatusCode.BadRequest,
            "This note can no longer be put back. Undo lasts ten minutes; write the note again to keep it.");
        var toasts = Render<ToastHost>();
        var page = RenderMemory();
        page.WaitForAssertion(() => page.FindAll("aside .dv-memory-note").Should().HaveCount(3));

        page.Find("[aria-label='Remove the note: Prefers tea.']").Click();
        toasts.WaitForAssertion(() => toasts.FindAll(".dv-toast button").Should().Contain(button => button.TextContent.Trim() == "Undo"));
        toasts.FindAll(".dv-toast button").Single(button => button.TextContent.Trim() == "Undo").Click();

        toasts.WaitForAssertion(() => toasts.Find("[role=alert] .dv-toast-message").TextContent.Should().Contain("This note can no longer be put back."));
    }

    #endregion

    #region The Conversation

    [Fact]
    public void A_link_to_a_moment_opens_the_conversation_and_brings_that_message_into_view()
    {
        var module = JSInterop.SetupModule("./_content/Drover.Ui/Chat/MessageList.razor.js");

        Navigation.NavigateTo($"/talk/{botId}?message={messageId}");
        var page = Render<Talk>(parameters => parameters.Add(talk => talk.BotId, botId));

        page.WaitForAssertion(() => module.VerifyInvoke("reveal").Arguments[1].Should().Be($"dv-message-{messageId}"));
        page.Find($"#dv-message-{messageId}").GetAttribute("tabindex").Should().Be("-1", "the linked message can take the focus");
        page.FindAll("main article[tabindex]").Should().ContainSingle("no other message becomes focusable");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    public void Forget_on_a_remembered_card_removes_the_note_and_the_card_says_so(HttpStatusCode answer)
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new[]
        {
            new MessageResponse(messageId, threadId, 1, "assistant", null,
            [
                new("tool_call") { ToolCallId = noteId, ToolName = "memory_save", Arguments = """{"text":"Prefers tea."}""" },
            ], DateTimeOffset.UnixEpoch),
            new MessageResponse(Guid.CreateVersion7(), threadId, 2, "tool", null,
            [
                new("tool_result") { ToolCallId = noteId, Result = $$$"""{"note":{"id":"{{{ownNoteId}}}","text":"Prefers tea."}}""" },
            ], DateTimeOffset.UnixEpoch),
        });
        if (answer is HttpStatusCode.OK)
        {
            Server.Answer("DELETE", $"{memory}/notes/{ownNoteId}", new RemovedNoteResponse(ownNoteId, "sealed-sample-token"));
        }
        else
        {
            Server.Answer("DELETE", $"{memory}/notes/{ownNoteId}", answer);
        }

        Navigation.NavigateTo($"/talk/{botId}");
        var page = Render<Talk>(parameters => parameters.Add(talk => talk.BotId, botId));
        page.WaitForAssertion(() => page.Find(".dv-disclosure-title").TextContent.Trim().Should().Be("Remembered: Prefers tea."));

        page.Find("[aria-label='Forget the note: Prefers tea.']").Click();

        page.WaitForAssertion(() => page.Find(".dv-tool-forgotten").TextContent.Should().Be("Forgotten. The note is no longer kept."));
        Server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{memory}/notes/{ownNoteId}");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Talk> RenderMemory()
    {
        Navigation.NavigateTo($"/talk/{botId}/memory");
        return Render<Talk>(parameters => parameters.Add(page => page.BotId, botId).Add(page => page.Rail, "memory"));
    }

    #endregion
}
