namespace Drover.Adapters.Tests;

/// <summary>The SearXNG search adapter against recorded answers of its JSON search API, offline.</summary>
public sealed class SearXngConformanceTests : WebSearchConformance
{
    #region Rig

    protected override SearchRig Rig(SearchServiceAnswer answer)
    {
        HttpMessageHandler handler = answer switch
        {
            SearchServiceAnswer.Unreachable => new ScriptedHandler((_, _, _) => throw new HttpRequestException("No route to the search service.")),

            // As the checked outbound handler fails a connection it refuses: the refusal inside a request failure.
            SearchServiceAnswer.Blocked => new ScriptedHandler((_, _, _) => throw new HttpRequestException(
                "The connection was refused.",
                new OutboundBlockedException("This server does not allow requests to private addresses."))),
            _ => new RecordedHandler(RecordedFixture.Load("searxng", FixtureFor(answer))),
        };
        var search = new SearXngSearch(
            new FixtureHttpClientFactory(handler),
            Options.Create(new SearXngOptions { BaseUrl = new Uri("http://search.example:8080/") }),
            NullLogger<SearXngSearch>.Instance);
        return new SearchRig(search, () => handler is RecordedHandler recorded ? [.. recorded.Requests.Select(request => request.Uri)] : []);
    }

    #endregion

    #region Wire

    [Fact]
    public async Task The_adapter_asks_the_configured_instance_for_json_with_safe_search()
    {
        var rig = Rig(SearchServiceAnswer.Results);

        await rig.Search.Search(new WebSearchRequest("hay & straw", 5, 1024 * 1024), TestContext.Current.CancellationToken);

        var sent = rig.Sent().Should().ContainSingle().Subject;
        sent.GetLeftPart(UriPartial.Path).Should().Be("http://search.example:8080/search");
        sent.Query.Should().Contain("q=hay%20%26%20straw").And.Contain("format=json").And.Contain("safesearch=1");
    }

    #endregion

    #region Private Methods

    private static string FixtureFor(SearchServiceAnswer answer) => answer switch
    {
        SearchServiceAnswer.Results => "results",
        SearchServiceAnswer.NoResults => "no_results",
        SearchServiceAnswer.Refused => "refused",
        SearchServiceAnswer.Busy => "busy",
        SearchServiceAnswer.Malformed => "malformed",
        _ => "unavailable",
    };

    #endregion
}
