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
        bench.Server.Answer("POST", search, new List<MessageSearchHit>
        {
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint the lighthouse.", [new(10, 10)]),
            new(repliedId, sideThreadId, botId, "Marshal", "Errands", false, true, sentAt, "The lighthouse is done.", [new(4, 10)]),
        });
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
        bench.Server.Answer("POST", search, new List<MessageSearchHit>
        {
            new(askedId, sideThreadId, botId, "Marshal", null, false, false, sentAt, "Paint.", []),
        });
        var model = Make();
        model.Query = "paint";

        await model.Search();

        model.Hits.Single().Facts.Should().StartWith("Side chat · ");
        model.Status.Should().Be("1 message found.");
    }

    [Fact]
    public async Task A_full_page_of_matches_says_how_to_narrow_them()
    {
        bench.Server.Answer("POST", search, Enumerable.Range(0, ConversationSearchViewModel.MaxShown)
            .Select(_ => new MessageSearchHit(Guid.CreateVersion7(), mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint.", []))
            .ToList());
        var model = Make();
        model.Query = "paint";

        await model.Search();

        model.Status.Should().Be("The 30 best matches. Add a word to narrow them.");
    }

    #endregion

    #region States

    [Fact]
    public async Task Searching_says_so_until_the_answer_comes()
    {
        var gate = bench.Server.Hold("POST", search);
        bench.Server.Answer("POST", search, new List<MessageSearchHit>());
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
        bench.Server.Answer("POST", search, new List<MessageSearchHit>());
        var model = Make();
        model.Query = "zebra";

        await model.Search();

        model.State.Should().Be(ConversationSearchState.NothingFound);
        model.NothingTitle.Should().Be("Nothing matches “zebra”");
        ConversationSearchViewModel.NothingCause.Should().Be("No message in your conversations has these words. Try other words, or fewer of them.");
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

        bench.Server.Answer("POST", search, new List<MessageSearchHit>
        {
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint.", []),
        });
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
        bench.Server.Answer("POST", search, new List<MessageSearchHit>
        {
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint.", []),
        });
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

        Server.Answer("GET", $"{Space}/model-connections", new[]
        {
            new ModelConnectionSummary(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null),
        });
        Server.Answer("GET", $"{Space}/bots", new[] { bot });
        Server.Answer("GET", $"{Space}/bots/{botId}/threads", new[] { main, side });
        Server.Answer("PUT", $"{Space}/bots/{botId}/main-thread", main);
        Server.Answer("GET", $"{Space}/threads/{mainThreadId}", main);
        Server.Answer("GET", $"{Space}/threads/{mainThreadId}/messages", mainHistory);
        Server.Answer("GET", $"{Space}/threads/{sideThreadId}", side);
        Server.Answer("GET", $"{Space}/threads/{sideThreadId}/messages", new[] { sideHitMessage });
        Server.Answer("POST", $"{Space}/threads/search", new List<MessageSearchHit>
        {
            new(askedId, mainThreadId, botId, "Marshal", null, true, false, sentAt, "Paint the lighthouse.", [new(10, 10)]),
            new(repliedId, sideThreadId, botId, "Marshal", "Errands", false, true, sentAt, "The lighthouse is done.", [new(4, 10)]),
        });

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
