namespace Drover.Adapters.Conformance.Web;

/// <summary>How the search service answers in a scenario. The adapter's test turns each into its service's real answer.</summary>
public enum SearchServiceAnswer
{
    /// <summary>Three results, best first: two web pages and one entry without a web address, which is left out.</summary>
    Results,

    NoResults,

    /// <summary>The service refuses the request, as one does whose JSON output is turned off or that asks for a key.</summary>
    Refused,

    /// <summary>The service is busy and asks to be tried later.</summary>
    Busy,

    Unavailable,

    /// <summary>The answer is not the service's JSON.</summary>
    Malformed,

    Unreachable,

    /// <summary>This server's outbound policy refuses the service's address before any request leaves.</summary>
    Blocked,
}


/// <summary>A search adapter aimed at a service that answers as asked, and the addresses of the requests it sent.</summary>
public sealed record SearchRig(IWebSearch Search, Func<IReadOnlyList<Uri>> Sent);


/// <summary>
/// What every <see cref="IWebSearch"/> must do. An adapter inherits this class and builds a rig for each answer. The
/// suite holds the promises the web.search tool rests on: results are titles, web addresses, and excerpts, best first
/// and never more than asked; an answer larger than allowed is refused, not read; every failure is a
/// <see cref="WebSearchException"/> whose message never repeats the query or the service's answer.
/// </summary>
public abstract class WebSearchConformance
{
    #region Private Fields

    private const string Query = "barn report 7f3a";

    private const long Generous = 1024 * 1024;

    #endregion

    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Results

    [Fact]
    public void The_search_has_a_kind()
    {
        Rig(SearchServiceAnswer.Results).Search.Kind.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Results_are_titles_web_addresses_and_excerpts_best_first()
    {
        var rig = Rig(SearchServiceAnswer.Results);

        var results = await rig.Search.Search(new WebSearchRequest(Query, 10, Generous), Token);

        results.Should().HaveCount(2, "an entry without an http or https address is left out");
        results.Should().OnlyContain(result => result.Title.Length > 0 && result.Url.IsAbsoluteUri
            && (result.Url.Scheme == "http" || result.Url.Scheme == "https"));
        results[0].Snippet.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task No_more_results_are_returned_than_were_asked_for()
    {
        var rig = Rig(SearchServiceAnswer.Results);

        var all = await rig.Search.Search(new WebSearchRequest(Query, 10, Generous), Token);
        var one = await rig.Search.Search(new WebSearchRequest(Query, 1, Generous), Token);

        one.Should().ContainSingle().Which.Should().Be(all[0], "the best result comes first");
    }

    [Fact]
    public async Task The_query_reaches_the_service()
    {
        var rig = Rig(SearchServiceAnswer.Results);

        await rig.Search.Search(new WebSearchRequest(Query, 5, Generous), Token);

        Uri.UnescapeDataString(rig.Sent().Should().ContainSingle().Subject.Query.Replace('+', ' ')).Should().Contain(Query);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_is_an_empty_list()
    {
        var rig = Rig(SearchServiceAnswer.NoResults);

        (await rig.Search.Search(new WebSearchRequest(Query, 5, Generous), Token)).Should().BeEmpty();
    }

    #endregion

    #region Failures

    [Theory]
    [InlineData(SearchServiceAnswer.Refused, WebSearchFailure.Refused)]
    [InlineData(SearchServiceAnswer.Busy, WebSearchFailure.Unavailable)]
    [InlineData(SearchServiceAnswer.Unavailable, WebSearchFailure.Unavailable)]
    [InlineData(SearchServiceAnswer.Unreachable, WebSearchFailure.Unavailable)]
    [InlineData(SearchServiceAnswer.Malformed, WebSearchFailure.Malformed)]
    [InlineData(SearchServiceAnswer.Blocked, WebSearchFailure.Blocked)]
    public async Task A_failure_is_a_search_exception_that_never_repeats_the_query(SearchServiceAnswer answer, WebSearchFailure failure)
    {
        var rig = Rig(answer);

        var search = () => rig.Search.Search(new WebSearchRequest(Query, 5, Generous), Token);

        var thrown = (await search.Should().ThrowAsync<WebSearchException>()).Which;
        thrown.Failure.Should().Be(failure);
        thrown.Message.Should().NotBeNullOrWhiteSpace().And.NotContain(Query);
    }

    [Fact]
    public async Task An_answer_larger_than_allowed_is_refused_not_read()
    {
        var rig = Rig(SearchServiceAnswer.Results);

        var search = () => rig.Search.Search(new WebSearchRequest(Query, 5, 64), Token);

        (await search.Should().ThrowAsync<WebSearchException>()).Which.Failure.Should().Be(WebSearchFailure.TooLarge);
    }

    [Fact]
    public async Task Cancellation_throws_and_is_never_reported_as_a_failure()
    {
        var rig = Rig(SearchServiceAnswer.Results);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var search = () => rig.Search.Search(new WebSearchRequest(Query, 5, Generous), cancelled.Token);

        await search.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Rig

    /// <summary>A search adapter whose service answers as <paramref name="answer"/> says, with a fresh record of requests.</summary>
    protected abstract SearchRig Rig(SearchServiceAnswer answer);

    #endregion
}
