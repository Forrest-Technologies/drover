namespace Drover.Adapters.Tests;

/// <summary>What the Venice adapter makes of responses: reasoning, citations, balances, and the wait to retry.</summary>
public sealed class VeniceResponseTests
{
    #region Reasoning

    /// <summary>
    /// Answers the issue's first question for the pinned client library: the compatible client reads
    /// Venice's reasoning_content field, in both whole and streamed replies, with nothing added by the adapter.
    /// </summary>
    [Fact]
    public async Task Surfaces_the_reasoning_content_field_as_reasoning_parts()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.ReasoningText);
        var client = rig.Subject.CreateChatClient();

        var whole = await client.Ask("What is 15 percent of 240?");

        whole.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Single().Text
            .Should().Be("10 percent is 24 and 5 percent is 12, so 36.");
        whole.Text.Should().Be("15 percent of 240 is 36.");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.Stream("What is 15 percent of 240?"))
        {
            updates.Add(update);
        }

        string.Concat(updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().Select(part => part.Text))
            .Should().Be("10 percent is 24, and 5 percent is 12, so 36.");
        updates.ToChatResponse().Text.Should().Be("15 percent of 240 is 36.");
    }

    [Fact]
    public async Task Marks_the_encrypted_placeholder_as_not_available_in_a_whole_reply()
    {
        var rig = VeniceRig.Start("encrypted_reasoning");

        var response = await rig.Subject.CreateChatClient().Ask("What is 15 percent of 240?");

        var reasoning = response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Single();
        reasoning.Text.Should().BeEmpty();
        reasoning.AdditionalProperties.Should().ContainKey(ModelResponseKeys.ReasoningNotAvailable)
            .WhoseValue.Should().Be(true);
        response.Text.Should().Be("It is 36.");
    }

    [Fact]
    public async Task Marks_the_encrypted_placeholder_as_not_available_while_streaming()
    {
        var rig = VeniceRig.Start("encrypted_reasoning");
        var client = rig.Subject.CreateChatClient();

        // The fixture answers the first chat request with a whole reply and the second with a stream.
        await client.Ask("What is 15 percent of 240?");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.Stream("What is 15 percent of 240?"))
        {
            updates.Add(update);
        }

        var reasoning = updates.SelectMany(update => update.Contents).OfType<TextReasoningContent>().ToList();
        reasoning.Should().NotBeEmpty();
        reasoning.Should().OnlyContain(part => part.Text == string.Empty);
        reasoning.Should().OnlyContain(part => part.AdditionalProperties!.ContainsKey(ModelResponseKeys.ReasoningNotAvailable));
        updates.ToChatResponse().Text.Should().Be("It is 36.");
    }

    #endregion

    #region Citations

    [Fact]
    public async Task Turns_web_search_citations_into_source_links_in_a_whole_reply()
    {
        var rig = VeniceRig.Start("web_search_citations");

        var response = await rig.Subject.CreateChatClient().Ask("What is the weather in Paris?");

        SourceLinks(response.Messages.SelectMany(message => message.Contents)).Should().Equal(
            ("Paris weather today", "https://weather.example.test/paris"),
            ("Climate of Paris", "https://climate.example.test/paris"));
        response.Text.Should().Be("It is sunny in Paris.");
    }

    [Fact]
    public async Task Turns_web_search_citations_into_source_links_while_streaming()
    {
        var rig = VeniceRig.Start("web_search_citations");
        var client = rig.Subject.CreateChatClient();

        await client.Ask("What is the weather in Paris?");

        List<ChatResponseUpdate> updates = [];
        await foreach (var update in client.Stream("What is the weather in Paris?"))
        {
            updates.Add(update);
        }

        SourceLinks(updates.SelectMany(update => update.Contents)).Should().Equal(
            ("Paris weather today", "https://weather.example.test/paris"),
            ("Climate of Paris", "https://climate.example.test/paris"));
        updates.ToChatResponse().Text.Should().Be("It is sunny in Paris.");
    }

    #endregion

    #region Balance and Limits

    [Fact]
    public async Task Reports_the_balances_in_the_response_headers()
    {
        var rig = VeniceRig.Start("balance_headers");

        var response = await rig.Subject.CreateChatClient().Ask("Say hello.");

        response.AdditionalProperties.Should().ContainKey(ModelResponseKeys.Balances);
        ((IReadOnlyList<ModelBalance>)response.AdditionalProperties![ModelResponseKeys.Balances]!).Should().BeEquivalentTo(
            [new ModelBalance("usd", 4.20m), new ModelBalance("diem", 12.5m)]);
    }

    [Fact]
    public async Task Reports_no_balance_when_the_headers_carry_none()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.PlainReply);

        var response = await rig.Subject.CreateChatClient().Ask("Say hello.");

        response.AdditionalProperties.Should().BeNull();
    }

    [Fact]
    public async Task Tells_the_caller_how_long_the_rate_limit_lasts_from_the_reset_header()
    {
        var rig = VeniceRig.Start(ConformanceScenarios.RateLimited);

        var failure = await FluentActions
            .Awaiting(() => rig.Subject.CreateChatClient().Ask("Say hello."))
            .Should().ThrowAsync<ModelProviderException>();

        failure.Which.RetryAfter.Should().BeCloseTo(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("1794400045000", 1794400000000L, 45)]
    [InlineData("1794400000000", 1794400000000L, 0)]
    [InlineData("1794399999000", 1794400000000L, 0)]
    public void Reads_the_reset_header_as_unix_milliseconds(string header, long nowMilliseconds, int expectedSeconds)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMilliseconds);

        VeniceHeaders.WaitUntilReset(header, now).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    public void Reads_an_unusable_reset_header_as_unknown(string? header)
    {
        VeniceHeaders.WaitUntilReset(header, DateTimeOffset.UtcNow).Should().BeNull();
    }

    #endregion

    #region Helpers

    private static IEnumerable<(string? Title, string? Url)> SourceLinks(IEnumerable<AIContent> contents) =>
        contents
            .SelectMany(content => content.Annotations ?? [])
            .OfType<CitationAnnotation>()
            .Select(citation => (citation.Title, citation.Url?.AbsoluteUri));

    #endregion
}
