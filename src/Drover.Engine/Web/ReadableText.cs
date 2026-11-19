namespace Drover.Engine.Web;

/// <summary>
/// The readable text of a page: what a person would read, without scripts, styles, forms, navigation, or markup. A
/// page's main or article element is read when it has one, so the text is the page's content and not its menus. Block
/// elements start a new line, list items start with a dash, and runs of white space are made one. The page is parsed,
/// never run: nothing it holds is fetched or executed.
/// </summary>
public static class ReadableText
{
    #region Private Fields

    private const string HiddenElements =
        "script, style, noscript, template, svg, canvas, iframe, object, embed, form, nav, header, footer, aside, button, select, [hidden], [aria-hidden=true]";

    private static readonly FrozenSet<string> blocks = new[]
    {
        "address", "article", "blockquote", "dd", "details", "div", "dl", "dt", "figcaption", "figure", "h1", "h2",
        "h3", "h4", "h5", "h6", "hr", "li", "main", "ol", "p", "pre", "section", "summary", "table", "tbody",
        "tfoot", "thead", "tr", "ul",
    }.ToFrozenSet(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    /// <summary>The page's title, if it has one, and its readable text.</summary>
    public static (string? Title, string Text) FromHtml(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var title = Tidy(document.Title ?? string.Empty, keepBlankLines: false);

        foreach (var hidden in document.QuerySelectorAll(HiddenElements).ToList())
        {
            hidden.Remove();
        }

        var content = document.QuerySelector("main") ?? document.QuerySelector("article") ?? document.Body ?? document.DocumentElement;
        return (title.Length > 0 ? title : null, Tidy(Collect(content), keepBlankLines: false));
    }

    /// <summary>Plain text as it is, with runs of white space made one and blank lines kept to one in a row.</summary>
    public static string FromPlainText(string text) => Tidy(text, keepBlankLines: true);

    #endregion

    #region Private Methods

    /// <summary>
    /// The text of every node beneath the root, in order. Walked with a stack, not by recursion, so a page nested
    /// thousands of levels deep cannot exhaust the thread's stack.
    /// </summary>
    private static string Collect(INode root)
    {
        var text = new StringBuilder();
        var pending = new Stack<(INode Node, bool Closing)>();
        PushChildren(pending, root);

        while (pending.TryPop(out var item))
        {
            var (node, closing) = item;

            if (closing)
            {
                text.Append('\n');
                continue;
            }

            switch (node)
            {
                case IText piece:
                    text.Append(piece.Data);
                    break;
                case IElement { LocalName: "br" }:
                    text.Append('\n');
                    break;
                case IElement element:
                    Open(text, pending, element);
                    break;
            }
        }

        return text.ToString();
    }

    private static void Open(StringBuilder text, Stack<(INode Node, bool Closing)> pending, IElement element)
    {
        if (blocks.Contains(element.LocalName))
        {
            text.Append('\n');
            pending.Push((element, true));
        }
        else if (element.LocalName is "td" or "th")
        {
            text.Append(' ');
        }

        if (element.LocalName is "li")
        {
            text.Append("- ");
        }

        PushChildren(pending, element);
    }

    private static void PushChildren(Stack<(INode Node, bool Closing)> pending, INode parent)
    {
        for (var index = parent.ChildNodes.Length - 1; index >= 0; index--)
        {
            pending.Push((parent.ChildNodes[index], false));
        }
    }

    /// <summary>
    /// Each line with its white space made single spaces. A page's blocks are already one to a line, so its blank
    /// lines are dropped; plain text keeps one blank line where it had any, as its paragraphs.
    /// </summary>
    private static string Tidy(string text, bool keepBlankLines)
    {
        List<string> lines = [];

        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var tidy = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (tidy.Length > 0 || (keepBlankLines && lines.Count > 0 && lines[^1].Length > 0))
            {
                lines.Add(tidy);
            }
        }

        return string.Join('\n', lines).Trim();
    }

    #endregion
}
