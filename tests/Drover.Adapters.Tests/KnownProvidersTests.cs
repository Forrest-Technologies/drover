namespace Drover.Adapters.Tests;

/// <summary>The table of named providers: what it holds, and what a connection made from a row sends and to where.</summary>
public sealed class KnownProvidersTests
{
    #region Private Fields

    private static readonly string[] lead = ["openai", "openrouter", "gemini", "xai", "groq", "deepseek", "mistral"];

    #endregion

    #region Properties

    public static TheoryData<string> Ids => [.. KnownProviders.All.Select(row => row.Id)];

    #endregion

    #region Table

    [Fact]
    public void Every_row_has_a_unique_id_and_a_safe_address()
    {
        KnownProviders.All.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        KnownProviders.All.Should().OnlyContain(row => row.Id.Length > 0
            && row.Id.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-'));
        KnownProviders.All.Should().OnlyContain(row => row.DisplayName.Trim().Length > 0);

        foreach (var row in KnownProviders.All)
        {
            var address = new Uri(row.Address, UriKind.Absolute);

            address.Scheme.Should().Be(row.Local ? address.Scheme : Uri.UriSchemeHttps, row.Id);
            address.Scheme.Should().BeOneOf(Uri.UriSchemeHttp, Uri.UriSchemeHttps);
            address.UserInfo.Should().BeEmpty(row.Id);
            address.Query.Should().BeEmpty(row.Id);
            address.Fragment.Should().BeEmpty(row.Id);

            if (row.KeyPage is not null)
            {
                new Uri(row.KeyPage, UriKind.Absolute).Scheme.Should().Be(Uri.UriSchemeHttps, row.Id);
            }
        }
    }

    [Fact]
    public void A_local_row_needs_no_key()
    {
        var local = KnownProviders.All.Where(row => row.Local).ToList();

        local.Should().NotBeEmpty();
        local.Should().OnlyContain(row => row.KeyRule != ModelProviderKeyRule.Required);
        local.Select(row => row.KeyPage).Should().OnlyContain(page => page == null);
        KnownProviders.All.Where(row => !row.Local).Should().OnlyContain(row => row.KeyRule == ModelProviderKeyRule.Required);
    }

    [Fact]
    public void The_rows_come_in_the_order_a_person_expects()
    {
        var ids = KnownProviders.All.Select(row => row.Id).ToList();
        var hosted = KnownProviders.All.Where(row => !row.Local && !lead.Contains(row.Id)).Select(row => row.DisplayName).ToList();

        ids.Take(lead.Length).Should().Equal(lead);
        hosted.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        KnownProviders.All.SkipWhile(row => !row.Local).Should().OnlyContain(row => row.Local, "local rows come last");
        KnownProviders.All.TakeWhile(row => !row.Local).Should().OnlyContain(row => !row.Local);
    }

    [Fact]
    public void The_provider_offers_the_table_in_the_same_order()
    {
        OpenAICompatibleProvider.Presets.Should().Equal(KnownProviders.All);
    }

    #endregion

    #region Wire

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task Every_row_sends_chat_to_its_own_address_with_a_bearer_key(string id)
    {
        var row = KnownProviders.All.Single(candidate => candidate.Id == id);
        List<HttpRequestMessage> sent = [];
        var handler = new ScriptedHandler((request, _, _) =>
        {
            sent.Add(request);
            return Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, ScriptedHandler.ChatReply));
        });
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        var key = TestKeys.Generate();
        var endpoint = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, new Uri(row.Address));

        await provider.CreateChatClient(endpoint, key, "any-model", new ModelSettings()).Ask("Say hello.");
        var models = await provider.ListModels(endpoint, key, TestContext.Current.CancellationToken);

        var expectedBase = row.Address.TrimEnd('/');
        sent.Should().HaveCount(2);
        sent.Select(request => request.RequestUri!.GetLeftPart(UriPartial.Path))
            .Should().BeEquivalentTo([$"{expectedBase}/chat/completions", $"{expectedBase}/models"]);
        sent.Should().OnlyContain(request => request.RequestUri!.Host == new Uri(row.Address).Host);
        sent.Should().OnlyContain(request => request.Headers.Authorization!.Scheme == "Bearer" && request.Headers.Authorization.Parameter == key);
        models.Should().BeEmpty();
    }

    #endregion
}
