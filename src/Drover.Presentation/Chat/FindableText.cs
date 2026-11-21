namespace Drover.Ui.Chat;

/// <summary>The control that draws a searched piece of text, its place among that kind of piece, and a path for text
/// drawn inside another bot's work. The path keeps two pieces of the same kind in delegated work distinct.</summary>
public sealed record FindPart(string Name, int Index = 0, string? Path = null);

/// <summary>One searchable piece of a message, in the order the conversation draws it.</summary>
public sealed record FindSegment(string MessageKey, FindPart Part, string Text);

/// <summary>
/// The text a conversation can search, taken from the words, cards, files and sources the message draws. None of it
/// is interface chrome: names beside replies, times, buttons and usage numbers are not here. The cache belongs to
/// this instance so a conversation can clear it when it is left, and its signature covers delegated work too.
/// </summary>
public sealed class FindableText
{
    #region Private Fields

    private readonly Dictionary<(string Key, int Signature), IReadOnlyList<FindSegment>> cache = [];

    #endregion

    #region Public Methods

    public IReadOnlyList<FindSegment> Of(ChatMessage message)
    {
        var key = (message.Key, Signature(message));

        if (!cache.TryGetValue(key, out var segments))
        {
            segments = Segments(message);
            cache[key] = segments;
        }

        return segments;
    }

    public void Clear() => cache.Clear();

    #endregion

    #region Private Methods

    // A hash of every piece of drawn text, including delegated children, so an edit of the same length and a
    // growing child both invalidate the entry. The hash is only a cache key inside this process.
    private static int Signature(ChatMessage message)
    {
        HashCode hash = new();
        hash.Add(message.Text);
        hash.Add(message.Reasoning);

        foreach (var source in message.Sources)
        {
            hash.Add(source.Title);
        }

        foreach (var file in message.Files)
        {
            hash.Add(file.Name);
            hash.Add(file.Caption);
            hash.Add(file.Preview);
        }

        foreach (var tool in message.Tools)
        {
            hash.Add(tool.Label);
            hash.Add(tool.Summary);
            hash.Add(tool.Arguments);
            hash.Add(tool.Result);

            foreach (var piece in tool.Output)
            {
                hash.Add(piece.Text);
            }

            foreach (var fact in tool.Facts)
            {
                hash.Add(fact.Name);
                hash.Add(fact.Value);
            }

            if (tool.Delegation is { } delegation)
            {
                foreach (var child in delegation.Work)
                {
                    hash.Add(Signature(child));
                }
            }
        }

        return hash.ToHashCode();
    }

    private List<FindSegment> Segments(ChatMessage message)
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
                Add(segments, message, new("ToolFact", factIndex, index.ToString(CultureInfo.InvariantCulture)), $"{fact.Name}: {fact.Value}");
            }

            if (tool.Delegation is { Work.Count: > 0 } delegation)
            {
                foreach (var (child, workIndex) in delegation.Work.Select((child, index) => (child, index)))
                {
                    foreach (var segment in Of(child))
                    {
                        var childPath = segment.Part.Path ?? segment.Part.Index.ToString(CultureInfo.InvariantCulture);
                        Add(segments, message, new FindPart($"Delegation{segment.Part.Name}", segment.Part.Index, $"{index}.{workIndex}.{childPath}"), segment.Text);
                    }
                }
            }
        }

        foreach (var (file, index) in message.Files.Select((file, index) => (file, index)))
        {
            Add(segments, message, new("FileName", index), file.Name);
            Add(segments, message, new("FileCaption", index), file.Caption);

            foreach (var (line, lineIndex) in (file.Preview ?? string.Empty).Split('\n').Select((line, index) => (line, index)))
            {
                Add(segments, message, new("FilePreview", lineIndex, index.ToString(CultureInfo.InvariantCulture)), line);
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
