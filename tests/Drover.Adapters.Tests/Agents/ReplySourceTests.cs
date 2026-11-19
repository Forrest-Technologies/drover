namespace Drover.Adapters.Tests.Agents;

/// <summary>
/// A reply's reasoning and the sources it cites are stored with it, in Drover's own part shape, so they are there when
/// the conversation is read again. A provider hands sources over as citations on a piece of text, often an empty piece
/// after the words; only web addresses are kept, because a source is something a person opens.
/// </summary>
public sealed class ReplySourceTests
{
    #region Private Fields

    private static readonly Guid runId = Guid.CreateVersion7();

    #endregion

    #region Tests

    [Fact]
    public void A_streamed_reply_keeps_its_reasoning_its_words_and_the_web_sources_it_cites()
    {
        ChatResponseUpdate[] streamed =
        [
            Update(new TextReasoningContent("Look at the sample page first.")),
            Update(new TextContent("The sample answer.")),
            Update(Cited(
                ("Sample page", "https://sample.example/page"),
                (null, "http://sample.example/other"),
                ("Not a web address", "ftp://sample.example/file"))),
        ];

        var stored = Store(streamed.ToChatResponse().Messages.Single());

        stored.Parts.Should().Equal(
            new ReasoningPart("Look at the sample page first."),
            new TextPart("The sample answer."),
            new SourcePart(new("https://sample.example/page"), "Sample page"),
            new SourcePart(new("http://sample.example/other")));
    }

    [Fact]
    public void A_reply_with_neither_reasoning_nor_sources_stores_only_its_words()
    {
        var stored = Store(new ChatMessage(ChatRole.Assistant, [new TextContent("Plain words.")]));

        stored.Parts.Should().Equal(new TextPart("Plain words."));
    }

    [Fact]
    public void A_source_survives_being_written_and_read_back_and_is_not_given_back_to_the_model()
    {
        var stored = Store(new ChatMessage(ChatRole.Assistant, [new TextContent("Cited words."), Cited(("  Sample   title ", "https://sample.example/"))]));

        var json = JsonSerializer.Serialize(stored.Parts);
        var read = JsonSerializer.Deserialize<List<MessagePart>>(json);

        json.Should().Contain("\"type\":\"source\"");
        read.Should().Equal(stored.Parts);
        stored.ToChatMessage().Contents.Should().ContainSingle().Which.Should().BeOfType<TextContent>().Which.Text.Should().Be("Cited words.");
    }

    #endregion

    #region Private Methods

    private static ChatResponseUpdate Update(AIContent content) => new(ChatRole.Assistant, [content]) { MessageId = "sample" };

    private static TextContent Cited(params (string? Title, string Url)[] citations) => new(string.Empty)
    {
        Annotations = [.. citations.Select(citation => new CitationAnnotation { Title = citation.Title, Url = new(citation.Url) })],
    };

    private static Message Store(ChatMessage message) =>
        message.ToMessage(runId, Guid.CreateVersion7(), Guid.CreateVersion7())!;

    #endregion
}
