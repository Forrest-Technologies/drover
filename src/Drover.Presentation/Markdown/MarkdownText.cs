namespace Drover.Ui.Markdown;

/// <summary>
/// The inline styling a drawn piece of markdown carries. It mirrors the flags <c>MarkdownView</c> uses.
/// </summary>
public sealed record MarkdownInlineStyle(
    bool Strong = false,
    bool Emphasis = false,
    bool Struck = false,
    bool Machine = false);

/// <summary>One drawn piece of text and the style it is drawn with.</summary>
public sealed record MarkdownSpan(string Text, MarkdownInlineStyle Style);

/// <summary>One block of drawn text, as a sequence of spans. Line breaks are characters in the text.</summary>
public sealed record MarkdownBlock(IReadOnlyList<MarkdownSpan> Spans)
{
    #region Properties

    // The plain concatenation is the source of truth. Highlight offsets are positions in this string, so it must
    // never be normalized away from the spans the view draws.
    public string Text => string.Concat(Spans.Select(span => span.Text));

    #endregion
}

/// <summary>
/// The text the native view draws from parsed markdown, in the same order. This is what conversation find matches
/// against, so markup characters are never searched and words split across styles stay one searchable piece.
/// </summary>
public static class MarkdownText
{
    #region Private Fields

    private static readonly HashSet<string> blockElements =
    [
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "blockquote", "table",
    ];

    #endregion

    #region Public Methods

    public static IReadOnlyList<MarkdownBlock> Blocks(IReadOnlyList<MarkdownNode> nodes)
    {
        List<MarkdownBlock> blocks = [];
        List<MarkdownSpan> inline = [];

        foreach (var node in nodes)
        {
            if (node is CodeNode code)
            {
                Flush();
                blocks.Add(new([new(code.Code, new(Machine: true))]));
            }
            else if (node is ElementNode { Name: "table" } table)
            {
                Flush();
                blocks.Add(new(Cells(table)));
            }
            else if (node is ElementNode { Name: "hr" })
            {
                Flush();
            }
            else if (node is ElementNode block && blockElements.Contains(block.Name))
            {
                Flush();
                blocks.Add(new(Edges(Spans([block], new()))));
            }
            else
            {
                inline.AddRange(Spans([node], new()));
            }
        }

        Flush();
        return blocks;

        void Flush()
        {
            if (inline.Count > 0)
            {
                var text = string.Concat(inline.Select(span => span.Text));

                if (!string.IsNullOrWhiteSpace(text))
                {
                    blocks.Add(new(Edges(inline)));
                }

                inline.Clear();
            }
        }
    }

    public static IReadOnlyList<MarkdownSpan> Spans(IReadOnlyList<MarkdownNode> nodes, MarkdownInlineStyle style)
    {
        List<MarkdownSpan> spans = [];
        var afterTask = false;

        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    // A task mark replaces the checkbox and its following space, so the two are not doubled.
                    var drawn = afterTask ? text.Text.TrimStart(' ') : text.Text;
                    afterTask = false;
                    spans.Add(new(drawn, style));
                    break;
                case ElementNode { Name: "br" }:
                    afterTask = false;
                    spans.Add(new("\n", style));
                    break;
                case ElementNode { Name: "strong" } strong:
                    spans.AddRange(Spans(strong.Children, style with { Strong = true }));
                    break;
                case ElementNode { Name: "em" } emphasis:
                    spans.AddRange(Spans(emphasis.Children, style with { Emphasis = true }));
                    break;
                case ElementNode { Name: "del" } removed:
                    spans.AddRange(Spans(removed.Children, style with { Struck = true }));
                    break;
                case ElementNode { Name: "code" } machine:
                    spans.AddRange(Spans(machine.Children, style with { Machine = true }));
                    break;
                case ElementNode element:
                    spans.AddRange(Spans(element.Children, style));
                    break;
                case LinkNode link:
                    spans.AddRange(Spans(link.Children, style));
                    break;
                case ImageNode image:
                    spans.Add(new(Placeholder(image), style));
                    break;
                case TaskNode task:
                    spans.Add(new(task.Done ? "Done: " : "Not done: ", style));
                    afterTask = true;
                    break;
                case CodeNode machine:
                    spans.Add(new(machine.Code, style with { Machine = true }));
                    break;
            }
        }

        return spans;
    }

    #endregion

    #region Private Methods

    // The parser puts line breaks at the edges of a block. Those are not drawn as part of the prose, so they are
    // dropped here. Interior text, including blank lines and the spaces in code, is kept exactly as drawn.
    private static List<MarkdownSpan> Edges(IReadOnlyList<MarkdownSpan> spans)
    {
        var first = 0;
        var last = spans.Count - 1;

        while (first <= last && string.IsNullOrWhiteSpace(spans[first].Text))
        {
            first++;
        }

        while (last >= first && string.IsNullOrWhiteSpace(spans[last].Text))
        {
            last--;
        }

        if (first > last)
        {
            return [];
        }

        List<MarkdownSpan> trimmed = [.. spans.Skip(first).Take(last - first + 1)];

        if (!trimmed[0].Style.Machine)
        {
            trimmed[0] = trimmed[0] with { Text = trimmed[0].Text.TrimStart() };
        }

        if (!trimmed[^1].Style.Machine)
        {
            trimmed[^1] = trimmed[^1] with { Text = trimmed[^1].Text.TrimEnd() };
        }

        return trimmed;
    }

    // The view draws an image as a button until its address is asked for. That label is the text on screen.
    private static string Placeholder(ImageNode image) =>
        string.IsNullOrWhiteSpace(image.Alt)
            ? $"Load image from {image.Source.Host}"
            : $"Load image from {image.Source.Host}: {image.Alt}";

    private static List<MarkdownSpan> Cells(ElementNode table)
    {
        var rows = table.Children.OfType<ElementNode>()
            .SelectMany(section => section.Name == "tr"
                ? [section]
                : section.Children.OfType<ElementNode>().Where(row => row.Name == "tr"));

        List<MarkdownSpan> spans = [];

        foreach (var (row, rowIndex) in rows.Select((row, index) => (row, index)))
        {
            if (rowIndex > 0)
            {
                spans.Add(new("\n", new()));
            }

            var cells = row.Children.OfType<ElementNode>().Where(cell => cell.Name is "th" or "td").ToList();

            foreach (var (cell, cellIndex) in cells.Select((cell, index) => (cell, index)))
            {
                if (cellIndex > 0)
                {
                    spans.Add(new("\t", new()));
                }

                spans.AddRange(Spans(cell.Children, new()));
            }
        }

        return spans;
    }

    #endregion
}
