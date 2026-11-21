namespace Drover.Ui.Chat;

/// <summary>The control that draws a searched piece of text, and its place among the pieces of that kind.</summary>
public sealed record FindPart(string Name, int Index = 0);

/// <summary>One searchable piece of a message, in the order the conversation draws it.</summary>
public sealed record FindSegment(string MessageKey, FindPart Part, string Text);

/// <summary>
/// The text a conversation can search, taken from the words, cards, files and sources the message draws. None of it
/// is interface chrome: names beside replies, times, buttons and usage numbers are not here. The result is cached by
/// message key and the length of its drawn text, so a reply that grows by one character recomputes only itself.
/// </summary>
public static class FindableText
{
    #region Private Fields

    private static readonly ConcurrentDictionary<(string Key, int Length), IReadOnlyList<FindSegment>> cache = new();

    #endregion

    #region Public Methods

    public static IReadOnlyList<FindSegment> Of(ChatMessage message) =>
        cache.GetOrAdd((message.Key, Length(message)), _ => Segments(message));

    #endregion

    #region Private Methods

    private static int Length(ChatMessage message)
    {
        var length = message.Text.Length + (message.Reasoning?.Length ?? 0) + message.Sources.Sum(source => source.Title.Length)
            + message.Files.Sum(file => file.Name.Length + (file.Caption?.Length ?? 0) + (file.Preview?.Length ?? 0));

        foreach (var tool in message.Tools)
        {
            length += tool.Label.Length + tool.Summary.Length + tool.Arguments.Length + (tool.Result?.Length ?? 0)
                + tool.Facts.Sum(fact => fact.Name.Length + fact.Value.Length) + tool.Output.Sum(piece => piece.Text.Length);
        }

        return length;
    }

    private static List<FindSegment> Segments(ChatMessage message)
    {
        List<FindSegment> segments = [];

        if (message.FromPerson)
        {
            Add(segments, message, new("Said"), message.Text);
        }
        else
        {
            foreach (var (block, index) in MarkdownText.Blocks(MarkdownDocument.Parse(message.Text)).Select((block, index) => (block, index)))
            {
                Add(segments, message, new("Markdown", index), block.Text);
            }
        }

        Add(segments, message, new("Reasoning"), message.Reasoning);

        foreach (var (source, index) in message.Sources.Select((source, index) => (source, index)))
        {
            Add(segments, message, new("SourceTitle", index), source.Title);
        }

        foreach (var (tool, index) in message.Tools.Select((tool, index) => (tool, index)))
        {
            Add(segments, message, new("ToolLabel", index), tool.Label);
            Add(segments, message, new("ToolSummary", index), tool.Summary);
            Add(segments, message, new("ToolArguments", index), tool.Arguments);
            Add(segments, message, new("ToolOutput", index), string.Concat(tool.Output.Select(piece => piece.Text)));
            Add(segments, message, new("ToolResult", index), tool.Result);

            foreach (var (fact, factIndex) in tool.Facts.Select((fact, index) => (fact, index)))
            {
                Add(segments, message, new("ToolFact", index * 1000 + factIndex), $"{fact.Name}: {fact.Value}");
            }

            if (tool.Delegation is { Work.Count: > 0 } delegation)
            {
                var workIndex = 0;

                foreach (var child in delegation.Work)
                {
                    foreach (var segment in Of(child))
                    {
                        Add(segments, message, new($"Delegation{segment.Part.Name}", index * 1000 + workIndex), segment.Text);
                    }

                    workIndex++;
                }
            }
        }

        foreach (var (file, index) in message.Files.Select((file, index) => (file, index)))
        {
            Add(segments, message, new("FileName", index), file.Name);
            Add(segments, message, new("FileCaption", index), file.Caption);

            foreach (var (line, lineIndex) in (file.Preview ?? string.Empty).Split('\n').Select((line, index) => (line, index)))
            {
                Add(segments, message, new("FilePreview", index * 1000 + lineIndex), line);
            }
        }

        return segments;
    }

    private static void Add(List<FindSegment> segments, ChatMessage message, FindPart part, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            segments.Add(new(message.Key, part, text));
        }
    }

    #endregion
}
