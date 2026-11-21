namespace Drover.Presentation.Tests.Chat;

/// <summary>What a conversation can search: the words the message draws and the text inside its cards and files, with
/// no interface chrome, grouped in drawing order and cached until the message's drawn text changes.</summary>
public sealed class FindableTextTests
{
    #region Public Methods

    [Fact]
    public void A_persons_words_are_searched_as_typed()
    {
        var message = new ChatMessage("m1", true, "recovery token");

        var segments = FindableText.Of(message);

        segments.Should().ContainSingle().Which.Should().Be(new FindSegment("m1", new FindPart("Said"), "recovery token"));
    }

    [Fact]
    public void A_reply_searches_the_drawn_markdown_without_its_markers()
    {
        var message = new ChatMessage("m2", false, "plain **bold** and `code`");

        var segments = FindableText.Of(message);

        segments.Should().ContainSingle().Which.Text.Should().Be("plain bold and code");
        segments[0].Part.Name.Should().Be("Markdown");
    }

    [Fact]
    public void The_order_follows_what_the_entry_draws()
    {
        var tool = new ToolCard(
            Guid.NewGuid(),
            ToolPhase.Done,
            "Ran a command",
            "run a command",
            "ls -la",
            """{"command":"ls -la"}""",
            true,
            "listed",
            0,
            [new ToolOutputPiece("stdout", "listed\n")])
        {
            Facts = [new ToolFact("What", "a listing")],
        };
        var file = new SharedFile(
            Guid.NewGuid(),
            "notes.txt",
            "text/plain",
            12,
            "/files/notes",
            false,
            DateTimeOffset.UnixEpoch,
            Caption: "the notes",
            Preview: "first\nsecond");
        var source = new SourceLink("A source", "https://example.test/a", "example.test");
        var message = new ChatMessage("m3", false, "words")
        {
            Reasoning = "thought",
            Tools = [tool],
            Files = [file],
            Sources = [source],
        };

        var names = FindableText.Of(message).Select(segment => segment.Part.Name).ToList();

        names.Should().Equal(
            "Markdown", "Reasoning", "SourceTitle",
            "ToolLabel", "ToolSummary", "ToolArguments", "ToolOutput", "ToolResult", "ToolFact",
            "FileName", "FileCaption", "FilePreview", "FilePreview");
    }

    [Fact]
    public void Delegated_work_is_searched_under_the_parent_message()
    {
        var child = new ChatMessage("child", true, "a task from the lead");
        var tool = new ToolCard(Guid.NewGuid(), ToolPhase.Done, "Handed work", "delegate", "Scout", "{}", true, null, null, [])
        {
            Delegation = new DelegationItem(
                Guid.NewGuid(), "Lead", Guid.NewGuid(), "Scout", "a task", DelegationPhase.Done, "done", DelegationRefusal.None, null, 1)
            {
                Work = [child],
            },
        };
        var message = new ChatMessage("parent", false, "words") { Tools = [tool] };

        var segments = FindableText.Of(message);

        segments.Should().ContainSingle(segment => segment.Text == "a task from the lead")
            .Which.MessageKey.Should().Be("parent");
    }

    [Fact]
    public void The_result_is_cached_until_the_drawn_text_changes()
    {
        var before = new ChatMessage("m4", true, "same words");
        var same = new ChatMessage("m4", true, "same words");
        var grown = new ChatMessage("m4", true, "same words more");

        FindableText.Of(same).Should().BeSameAs(FindableText.Of(before));
        FindableText.Of(grown).Should().NotBeSameAs(FindableText.Of(before));
    }

    #endregion
}
