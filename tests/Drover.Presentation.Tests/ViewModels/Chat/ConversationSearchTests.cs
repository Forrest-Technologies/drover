namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Searching the person's conversations, against the fake server: each state with its sentence and its one next
/// step, and a result that opens its conversation at the message it found.
/// </summary>
public sealed class ConversationSearchTests : IDisposable
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid mainThreadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid sideThreadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000006");

    private static readonly Guid askedId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid repliedId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly DateTimeOffset sentAt = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly string search = $"{ChatBench.Space}/threads/search";

    private readonly ChatBench bench = new();

    #endregion

    #region Public Methods

    public void Dispose() => bench.Dispose();

    #endregion

    #region Finding

    [Fact]
    public async Task Each_message_found_opens_its_conversation_at_that_message()
    {
        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint the lighthouse.", [new(10, 10)]),
            new(repliedId, sideThreadId, botId, "Marshal", "Errands", false, true, sentAt, "The lighthouse is done.", [new(4, 10)]),
        ]));
        var model = Make();
        model.Query = "  lighthouse & paint ";

        await model.Search();

        model.State.Should().Be(ConversationSearchState.Found);
        ConversationSearchScreen.For(model.State, model.IsActive).Should().Be(new ConversationSearchScreen(true, false, false, true));
        model.Searched.Should().Be("lighthouse & paint");
        bench.Server.Addresses.Should().ContainSingle(address => address.StartsWith("POST ")).Which
            .Should().Be($"POST {search}");
        JsonSerializer.Deserialize<SearchMessagesRequest>(bench.Server.Sent("POST", search).Single(), JsonSerializerOptions.Web)!
            .Query.Should().Be("lighthouse & paint");
        model.Status.Should().Be("2 messages found.");
        model.Hits.Select(hit => hit.Href).Should().Equal(
            $"/talk/{botId}#message-{askedId}",
            $"/talk/{botId}/side/{sideThreadId}#message-{repliedId}");
        var (asked, replied) = (model.Hits[0], model.Hits[1]);
        asked.Heading.Should().Be("Message to Marshal");
        asked.Facts.Should().Be($"Main conversation · {ScheduleWords.Moment(sentAt)}");
        asked.Matches.Should().Equal(new SnippetMatch(10, 10));
        replied.Heading.Should().Be("Reply from Marshal");
        replied.Facts.Should().StartWith("Errands · ");
        replied.AccessibleName.Should().Be($"Reply from Marshal in the side chat Errands, {ScheduleWords.Moment(sentAt)}: The lighthouse is done.");
        asked.AccessibleName.Should().StartWith("Message to Marshal in the main conversation, ");
        replied.BotHue.Should().Be(BotIdentity.Hue(botId));
        ArrowMotion.Next(0, model.Hits.Count, ArrowMotionKey.Next).Should().Be(1);
        model.Hits[1].AccessibleName.Should().Be(replied.AccessibleName);
        ArrowMotion.Next(1, model.Hits.Count, ArrowMotionKey.Previous).Should().Be(0);
        ArrowMotion.Next(1, model.Hits.Count, ArrowMotionKey.Next).Should().Be(1);
        ArrowMotion.Next(1, model.Hits.Count, ArrowMotionKey.First).Should().Be(0);
        ArrowMotion.Next(0, model.Hits.Count, ArrowMotionKey.Last).Should().Be(1);
    }

    [Fact]
    public async Task A_side_chat_with_no_title_is_named_as_a_side_chat()
    {
        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, sideThreadId, botId, "Marshal", null, false, false, sentAt, "Paint.", []),
        ]));
        var model = Make();
        model.Query = "paint";

        await model.Search();

        model.Hits.Single().Facts.Should().StartWith("Side chat · ");
        model.Status.Should().Be("1 message found.");
    }

    [Fact]
    public async Task A_full_page_of_matches_says_how_to_narrow_them()
    {
        bench.Server.Answer("POST", search, SearchPages.Page([.. Enumerable.Range(0, ConversationSearchViewModel.MaxShown)
            .Select(_ => new MessageSearchHit(Guid.CreateVersion7(), mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint.", []))]));
        var model = Make();
        model.Query = "paint";

        await model.Search();

        model.Status.Should().Be("The 30 best matches. Add a word to narrow them.");
    }

    [Fact]
    public async Task A_search_that_stopped_short_says_how_far_back_it_looked_and_never_claims_there_is_no_match()
    {
        var stoppedAt = new SearchCursor(sentAt.AddDays(-30), Guid.CreateVersion7());
        bench.Server.Answer("POST", search, SearchPages.Page([], stoppedAt));
        var model = Make();
        model.Query = "nautilus";

        await model.Search();

        model.State.Should().Be(ConversationSearchState.NothingFound);
        model.CanSearchOlder.Should().BeTrue();
        model.Status.Should().Be("Searched your newest 2,000 messages.");
        model.NothingExplanation.Should().Be("No message in your newest 2,000 messages has these words. Older messages are not searched until you ask.");
        model.NothingExplanation.Should().NotBe(ConversationSearchViewModel.NothingCause);
        ConversationSearchViewModel.OlderAction.Should().Be("Search older messages");

        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint the lighthouse.", [new(10, 10)]),
        ], stoppedAt));
        model.Query = "lighthouse";
        await model.Search();

        model.Status.Should().Be("1 message found. Searched your newest 2,000 messages.");
        model.CanSearchOlder.Should().BeTrue();
    }

    [Fact]
    public async Task Searching_older_messages_continues_from_where_the_search_stopped_and_adds_what_it_finds()
    {
        var stoppedAt = new SearchCursor(sentAt.AddDays(-30), Guid.CreateVersion7());
        bench.Server.Answer("POST", search, SearchPages.Page([], stoppedAt));
        var model = Make();
        model.Query = "nautilus";
        await model.Search();
        model.Hits.Should().BeEmpty();

        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt.AddDays(-45), "Ancient nautilus fossil.", [new(8, 8)]),
        ]));
        await model.SearchOlder();

        var sent = bench.Server.Sent("POST", search)
            .Select(body => JsonSerializer.Deserialize<SearchMessagesRequest>(body, JsonSerializerOptions.Web)!)
            .ToList();
        sent.Should().HaveCount(2);
        sent[0].Before.Should().BeNull();
        sent[1].Query.Should().Be("nautilus");
        sent[1].Before.Should().Be(stoppedAt);
        model.State.Should().Be(ConversationSearchState.Found);
        model.Hits.Select(hit => hit.Key).Should().Equal(askedId);
        model.Status.Should().Be("1 message found.", "every message has been searched, so nothing more is claimed");
        model.CanSearchOlder.Should().BeFalse();

        await model.SearchOlder();
        bench.Server.Sent("POST", search).Should().HaveCount(2, "there is nothing older to ask for");
    }

    [Fact]
    public async Task Trying_again_after_older_messages_failed_asks_for_the_same_page_and_keeps_the_results()
    {
        var stoppedAt = new SearchCursor(sentAt.AddDays(-30), Guid.CreateVersion7());
        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint the lighthouse.", [new(10, 10)]),
        ], stoppedAt));
        var model = Make();
        model.Query = "lighthouse";
        await model.Search();

        bench.Server.Answer("POST", search, HttpStatusCode.InternalServerError);
        await model.SearchOlder();

        model.State.Should().Be(ConversationSearchState.Failed);
        model.Hits.Should().ContainSingle("what was found stays while the next page is retried");
        ConversationSearchScreen.For(model.State, model.IsActive, model.Hits.Count > 0).Should().Be(new ConversationSearchScreen(true, true, false, true));

        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(repliedId, sideThreadId, botId, "Marshal", "Errands", false, true, sentAt.AddDays(-40), "The lighthouse is done.", [new(4, 10)]),
        ]));
        await model.Retry();

        var sent = bench.Server.Sent("POST", search)
            .Select(body => JsonSerializer.Deserialize<SearchMessagesRequest>(body, JsonSerializerOptions.Web)!)
            .ToList();
        sent.Should().HaveCount(3);
        sent[0].Before.Should().BeNull();
        sent[1].Before.Should().Be(stoppedAt);
        sent[2].Before.Should().Be(stoppedAt, "the retry asks for the page that failed, not for the newest messages");
        model.State.Should().Be(ConversationSearchState.Found);
        model.Hits.Select(hit => hit.Key).Should().Equal(askedId, repliedId);
    }

    [Fact]
    public async Task Trying_again_after_the_first_search_failed_searches_from_the_newest_messages()
    {
        bench.Server.Answer("POST", search, HttpStatusCode.InternalServerError);
        var model = Make();
        model.Query = "lighthouse";
        await model.Search();
        model.State.Should().Be(ConversationSearchState.Failed);
        model.Hits.Should().BeEmpty();

        bench.Server.Answer("POST", search, SearchPages.Page([]));
        await model.Retry();

        bench.Server.Sent("POST", search)
            .Select(body => JsonSerializer.Deserialize<SearchMessagesRequest>(body, JsonSerializerOptions.Web)!.Before)
            .Should().OnlyContain(before => before == null);
        model.State.Should().Be(ConversationSearchState.NothingFound);
    }

    [Fact]
    public async Task Mine_only_is_sent_with_the_search_and_changing_it_searches_again_from_the_newest_messages()
    {
        var stoppedAt = new SearchCursor(sentAt.AddDays(-30), Guid.CreateVersion7());
        bench.Server.Answer("POST", search, SearchPages.Page([], stoppedAt));
        var model = Make();
        model.Query = "lighthouse";
        await model.Search();
        model.MineOnly.Should().BeFalse("the search covers the shared conversations too until it is narrowed");

        await model.SetMineOnly(true);
        await model.SearchOlder();
        await model.SetMineOnly(false);

        var sent = bench.Server.Sent("POST", search)
            .Select(body => JsonSerializer.Deserialize<SearchMessagesRequest>(body, JsonSerializerOptions.Web)!)
            .ToList();
        sent.Select(request => request.MineOnly).Should().Equal([false, true, true, false]);
        sent.Select(request => request.Before).Should().Equal([null, null, stoppedAt, null], "turning it on or off starts again from the newest");
        ConversationSearchViewModel.MineOnlyLabel.Should().Be("Mine only");
    }

    [Fact]
    public async Task Mine_only_with_no_words_is_kept_for_the_next_search_and_asks_nothing_and_says_so_when_nothing_is_found()
    {
        bench.Server.Answer("POST", search, SearchPages.Page([]));
        var model = Make();

        model.NothingExplanation.Should().Be(ConversationSearchViewModel.NothingCause, "the search covers the shared conversations too until it is narrowed");
        await model.SetMineOnly(true);

        bench.Server.Sent("POST", search).Should().BeEmpty("there are no words to search for");
        model.MineOnly.Should().BeTrue();
        model.Query = "zebra";
        await model.Search();
        JsonSerializer.Deserialize<SearchMessagesRequest>(bench.Server.Sent("POST", search).Single(), JsonSerializerOptions.Web)!.MineOnly.Should().BeTrue();
        model.NothingExplanation.Should().Be(ConversationSearchViewModel.NothingMineCause);
    }

    [Fact]
    public async Task A_new_search_starts_again_from_the_newest_messages()
    {
        var stoppedAt = new SearchCursor(sentAt.AddDays(-30), Guid.CreateVersion7());
        bench.Server.Answer("POST", search, SearchPages.Page([], stoppedAt));
        var model = Make();
        model.Query = "nautilus";
        await model.Search();
        model.Query = "lighthouse";

        await model.Search();

        bench.Server.Sent("POST", search)
            .Select(body => JsonSerializer.Deserialize<SearchMessagesRequest>(body, JsonSerializerOptions.Web)!.Before)
            .Should().OnlyContain(before => before == null);
        model.Clear();
        model.CanSearchOlder.Should().BeFalse();
        model.Coverage.Should().BeNull();
    }

    #endregion

    #region States

    [Fact]
    public async Task Searching_says_so_until_the_answer_comes()
    {
        var gate = bench.Server.Hold("POST", search);
        bench.Server.Answer("POST", search, SearchPages.Page([]));
        var model = Make();
        model.Query = "paint";

        var searching = model.Search();

        model.State.Should().Be(ConversationSearchState.Searching);
        model.IsActive.Should().BeTrue();
        model.Status.Should().Be("Searching your conversations.");
        gate.SetResult();
        await searching;
        model.State.Should().Be(ConversationSearchState.NothingFound);
    }

    [Fact]
    public async Task Words_that_match_nothing_say_so_and_offer_to_clear_the_search()
    {
        bench.Server.Answer("POST", search, SearchPages.Page([]));
        var model = Make();
        model.Query = "zebra";

        await model.Search();

        model.State.Should().Be(ConversationSearchState.NothingFound);
        model.NothingTitle.Should().Be("Nothing matches “zebra”");
        ConversationSearchViewModel.NothingCause.Should().Be("No message in the conversations you can read has these words. Try other words, or fewer of them.");
        ConversationSearchViewModel.ClearAction.Should().Be("Clear the search");
        model.Status.Should().BeNull();
        model.Hits.Should().BeEmpty();
        ArrowMotion.Next(0, model.Hits.Count, ArrowMotionKey.Next).Should().BeNull();
        ConversationSearchScreen.For(model.State, model.IsActive).Should().Be(new ConversationSearchScreen(true, false, true, false));

        model.Clear();

        (model.State, model.IsActive, model.Query, model.Searched).Should().Be((ConversationSearchState.Idle, false, string.Empty, (string?)null));
        ConversationSearchScreen.For(model.State, model.IsActive).Should().Be(new ConversationSearchScreen(false, false, false, false));
    }

    [Fact]
    public async Task A_search_that_fails_says_why_and_works_on_a_second_try()
    {
        bench.Server.Answer("POST", search, HttpStatusCode.InternalServerError);
        var model = Make();
        model.Query = "paint";

        await model.Search();

        model.State.Should().Be(ConversationSearchState.Failed);
        model.Problem.Should().NotBeNullOrWhiteSpace();
        ConversationSearchViewModel.FailedTitle.Should().Be("Drover could not search your conversations");
        ConversationSearchScreen.For(model.State, model.IsActive).Should().Be(new ConversationSearchScreen(true, true, false, false));

        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint.", []),
        ]));
        await model.Search();

        model.State.Should().Be(ConversationSearchState.Found);
        model.Problem.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_words_put_the_crew_back_without_asking_the_server(string words)
    {
        var model = Make();
        model.Query = words;

        await model.Search();

        model.State.Should().Be(ConversationSearchState.Idle);
        bench.Server.Requests.Should().NotContain(request => request.Path == search);
    }

    [Fact]
    public async Task An_answer_that_comes_after_the_search_was_cleared_is_dropped()
    {
        var gate = bench.Server.Hold("POST", search);
        bench.Server.Answer("POST", search, SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint.", []),
        ]));
        var model = Make();
        model.Query = "paint";

        var searching = model.Search();
        model.Clear();
        gate.SetResult();
        await searching;

        model.State.Should().Be(ConversationSearchState.Idle);
        model.Hits.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private ConversationSearchViewModel Make() => bench.Make<ConversationSearchViewModel>();

    #endregion
}

/// <summary>Verifies that following a search hit navigates into TalkViewModel with that message selected as its moment.</summary>
public sealed class ConversationSearchNavigationTests : WorkspaceTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid mainThreadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid sideThreadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000006");

    private static readonly Guid askedId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid repliedId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly DateTimeOffset sentAt = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private const double RowHeight = 80;

    private const double Gap = 16;

    private const double Viewport = 480;

    #endregion

    #region Public Methods

    [Fact]
    public async Task Navigating_to_a_search_hit_opens_its_conversation_with_that_message_as_its_moment()
    {
        var bot = new BotSummary(botId, "Marshal", "marshal", "Answer briefly.", connectionId, "sample-model", "active");
        var main = new ThreadSummary(mainThreadId, botId, null, true, sentAt, null);
        var side = new ThreadSummary(sideThreadId, botId, "Errands", false, sentAt, null);
        var mainHitMessage = new MessageResponse(askedId, mainThreadId, 1, "user", null, [new MessagePartResponse("text") { Text = "Paint the lighthouse." }], sentAt);
        List<MessageResponse> mainHistory = [mainHitMessage];
        for (var index = 2; index <= 16; index++)
        {
            mainHistory.Add(new MessageResponse(
                Guid.Parse($"0196f1a0-0000-7000-8000-{index:000000000000}"),
                mainThreadId,
                index,
                index % 2 == 0 ? "assistant" : "user",
                null,
                [new MessagePartResponse("text") { Text = $"Later message {index}." }],
                sentAt));
        }
        var sideHitMessage = new MessageResponse(repliedId, sideThreadId, 1, "assistant", null, [new MessagePartResponse("text") { Text = "The lighthouse is done." }], sentAt);

        ModelConnectionSummary[] connections = [new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null)];
        BotSummary[] bots = [bot];
        ThreadSummary[] threads = [main, side];
        MessageResponse[] sideMessages = [sideHitMessage];

        Server.Answer("GET", $"{Space}/model-connections", connections);
        Server.Answer("GET", $"{Space}/bots", bots);
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", threads);
        Server.Answer("PUT", $"{Space}/bots/{botId}/main-thread", main);
        Server.Answer("GET", $"{Space}/threads/{mainThreadId}", main);
        Server.Answer("GET", $"{Space}/threads/{mainThreadId}/messages", mainHistory);
        Server.Answer("GET", $"{Space}/threads/{sideThreadId}", side);
        Server.Answer("GET", $"{Space}/threads/{sideThreadId}/messages", sideMessages);
        Server.Answer("POST", $"{Space}/threads/search", SearchPages.Page(
        [
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint the lighthouse.", [new(10, 10)]),
            new(repliedId, sideThreadId, botId, "Marshal", "Errands", false, true, sentAt, "The lighthouse is done.", [new(4, 10)]),
        ]));

        var search = new ConversationSearchViewModel(Server.CreateApi(), Session, NullLogger<ConversationSearchViewModel>.Instance)
        {
            Query = "lighthouse",
        };
        await Go("/");
        await Until(() => Talk.MainPane.Should().NotBeNull());

        await search.Search();
        search.Hits.Should().HaveCount(2);

        await Go(search.Hits[0].Href);
        await Until(() => Talk.MainPane!.List.Moment.Should().Be(askedId));
        (await Ui.Read(Talk.MainPane!.List.TakeMoment)).Should().Be(askedId);
        OpenedHitIsInView(Talk.MainPane.List.Messages, askedId).Should().BeTrue();
        FootHidesOpenedHit(Talk.MainPane.List.Messages, askedId).Should().BeTrue();

        await Go(search.Hits[1].Href);
        await Until(() => Talk.SidePane?.List.Moment.Should().Be(repliedId));
        Talk.Side.Should().Be(SideView.Chat);
        (await Ui.Read(Talk.SidePane!.List.TakeMoment)).Should().Be(repliedId);
    }

    #endregion

    #region Helpers

    // The same offset MessageList applies when a link names a message. Rows are a fixed height, so a long
    // conversation's foot hides the first message and the moment offset brings it back into the viewport.
    private static bool OpenedHitIsInView(IReadOnlyList<ChatMessage> messages, Guid hit)
    {
        var rows = Rows(messages);
        var hitRow = rows.Single(row => row.Id == hit);
        var offset = MomentViewport.Offset(hitRow.Top, hitRow.Height, Viewport, Scrollable(rows));
        return MomentViewport.Shows(offset, hitRow.Top, hitRow.Height, Viewport);
    }

    private static bool FootHidesOpenedHit(IReadOnlyList<ChatMessage> messages, Guid hit)
    {
        var rows = Rows(messages);
        var hitRow = rows.Single(row => row.Id == hit);
        return !MomentViewport.Shows(Scrollable(rows), hitRow.Top, hitRow.Height, Viewport);
    }

    private static List<(Guid Id, double Top, double Height)> Rows(IReadOnlyList<ChatMessage> messages)
    {
        List<(Guid Id, double Top, double Height)> rows = [];
        foreach (var message in messages)
        {
            if (Guid.TryParse(message.Key, out var id))
            {
                rows.Add((id, rows.Count * (RowHeight + Gap), RowHeight));
            }
        }

        return rows;
    }

    private static double Scrollable(IReadOnlyList<(Guid Id, double Top, double Height)> rows)
    {
        var extent = rows[^1].Top + rows[^1].Height;
        return Math.Max(0, extent - Viewport);
    }

    #endregion
}


/// <summary>The answers a search gets from the sample server.</summary>
file static class SearchPages
{
    #region Public Methods

    /// <summary>An answer that looked at the newest messages and, when <paramref name="older"/> is given, left older ones unsearched.</summary>
    public static MessageSearchResult Page(IReadOnlyList<MessageSearchHit> hits, SearchCursor? older = null) => new(hits, Window, older);

    public const int Window = 2000;

    #endregion
}
