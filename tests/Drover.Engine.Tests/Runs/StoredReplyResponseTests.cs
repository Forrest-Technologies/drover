namespace Drover.Engine.Tests.Runs;

/// <summary>
/// A stored reply's reasoning and sources reach clients as parts of their own kind: a source carries its web address
/// and its title, so a screen can show it as a link without reading any provider's shape.
/// </summary>
public sealed class StoredReplyResponseTests
{
    #region Tests

    [Fact]
    public void A_stored_reply_returns_its_reasoning_its_words_and_its_sources()
    {
        var reply = Reply(
            new ReasoningPart("A sample line of reasoning."),
            new TextPart("A sample answer."),
            new SourcePart(new("https://sample.example/page"), "Sample page"),
            new SourcePart(new("https://sample.example/untitled")));

        var parts = reply.ToResponse().Parts;

        parts.Select(part => (part.Kind, part.Text, part.Url)).Should().Equal(
            ("reasoning", "A sample line of reasoning.", null),
            ("text", "A sample answer.", null),
            ("source", "Sample page", "https://sample.example/page"),
            ("source", null, "https://sample.example/untitled"));
    }

    [Fact]
    public void A_reply_without_reasoning_or_sources_returns_neither()
    {
        var parts = Reply(new TextPart("A sample answer.")).ToResponse().Parts;

        parts.Should().ContainSingle().Which.Kind.Should().Be("text");
        parts.Single().Url.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static Message Reply(params MessagePart[] parts) => new()
    {
        ThreadId = Guid.CreateVersion7(),
        Role = MessageRole.Assistant,
        RunId = Guid.CreateVersion7(),
        Parts = [.. parts],
        CreatedAt = DateTimeOffset.UtcNow,
    };

    #endregion
}
