namespace Drover.Presentation.Tests.Chat;

/// <summary>What a conversation can search: the words the message draws and the text inside its cards and files, with
/// no interface chrome, grouped in drawing order and cached until any of that text changes.</summary>
public sealed class FindableTextTests
{
    #region Public Methods

    [Fact]
    public void A_persons_words_are_searched_as_typed()
    {
        var message = new ChatMessage("m1", true, "recovery token");

        var segments = new FindableText().Of(message);

        var expected = new FindSegment("m1", new FindPart("Said"), "recovery token");
        segments.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void A_reply_searches_the_drawn_markdown_without_its_markers()
    {
        var message = new ChatMessage("m2", false, "plain **bold** and `code`");

        var segments = new FindableText().Of(message);

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

        var names = new FindableText().Of(message).Select(segment => segment.Part.Name).ToList();

        names.Should().Equal(
            "Markdown", "Reasoning", "SourceTitle",
            "ToolLabel", "ToolSummary", "ToolArguments", "ToolOutput", "ToolResult", "ToolFact",
            "FileName", "FileCaption", "FilePreview", "FilePreview");
    }

    [Fact]
    public void Delegated_work_is_searched_under_the_parent_with_distinct_parts()
    {
        var child = new ChatMessage("child", false, "first **block**\n\nsecond block")
        {
            Tools = [new ToolCard(Guid.NewGuid(), ToolPhase.Done, "Ran one", "run", "one", "{}", true, null, null, [])],
        };
        var tool = new ToolCard(
            Guid.NewGuid(), ToolPhase.Done, "Handed work", "delegate", "Scout", "{}", true, null, null, [])
        {
            Delegation = new DelegationItem(
                Guid.NewGuid(),
                "Lead",
                Guid.NewGuid(),
                "Scout",
                "a task",
                DelegationPhase.Done,
                "done",
                DelegationRefusal.None,
                null,
                1)
            {
                Work = [child],
            },
        };
        var message = new ChatMessage("parent", false, "words") { Tools = [tool] };

        var segments = new FindableText().Of(message)
            .Where(segment => segment.Part.Name.StartsWith("Delegation", StringComparison.Ordinal))
            .ToList();

        segments.Should().Contain(segment => segment.Text == "first block" && segment.MessageKey == "parent");
        segments.Should().Contain(segment => segment.Text == "second block" && segment.MessageKey == "parent");
        segments.Where(segment => segment.Part.Name == "DelegationMarkdown").Select(segment => segment.Part)
            .Should().OnlyHaveUniqueItems();
        segments.Should().Contain(segment => segment.Part.Name == "DelegationToolLabel" && segment.Part.Path != null);
    }

    [Fact]
    public void A_growing_delegated_child_recomputes_the_parent_segments()
    {
        var child = new ChatMessage("child", true, "short");
        var tool = new ToolCard(
            Guid.NewGuid(), ToolPhase.Done, "Handed work", "delegate", "Scout", "{}", true, null, null, [])
        {
            Delegation = new DelegationItem(
                Guid.NewGuid(),
                "Lead",
                Guid.NewGuid(),
                "Scout",
                "a task",
                DelegationPhase.Done,
                "done",
                DelegationRefusal.None,
                null,
                1)
            {
                Work = [child],
            },
        };
        var before = new ChatMessage("parent", false, "words") { Tools = [tool] };
        var grownChild = child with { Text = "short and much longer" };
        var grown = new ChatMessage("parent", false, "words")
        {
            Tools = [tool with { Delegation = tool.Delegation with { Work = [grownChild] } }],
        };
        var findable = new FindableText();

        var beforeTexts = findable.Of(before).Select(segment => segment.Text).ToList();
        beforeTexts.Should().NotContain(text => text.Contains("much longer", StringComparison.Ordinal));
        findable.Of(grown).Should().Contain(segment => segment.Text.Contains("much longer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_same_length_edit_recomputes_the_segments()
    {
        var findable = new FindableText();

        findable.Of(new ChatMessage("m", true, "cat")).Single().Text.Should().Be("cat");
        findable.Of(new ChatMessage("m", true, "dog")).Single().Text.Should().Be("dog");
    }

    [Fact]
    public void The_cache_keeps_one_entry_per_message_and_can_be_cleared()
    {
        var findable = new FindableText();

        foreach (var length in Enumerable.Range(1, 20))
        {
            findable.Of(new ChatMessage("m", true, new string('a', length)));
            findable.Of(new ChatMessage("other", true, "fixed"));
            findable.Count.Should().Be(2, "each streamed state replaces the earlier entry for its message");
        }

        findable.Of(new ChatMessage("m", true, "latest")).Single().Text.Should().Be("latest");
        findable.Clear();
        findable.Count.Should().Be(0);
    }

    #endregion
}
