namespace Drover.Ui.Markdown;

/// <summary>
/// Parses a Markdown file for the native preview viewer, capping node count and depth and turning remote images into
/// plain external links whose target address is visible in the link text.
/// </summary>
public static class FileMarkdown
{
    #region Private Fields

    private const int MaxNodes = 512;

    private const int MaxDepth = 24;

    #endregion

    #region Public Methods

    public static IReadOnlyList<MarkdownNode>? Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > NativeFilePreview.MaxTextBytes)
        {
            return null;
        }

        var nodes = MarkdownDocument.Parse(text);
        var remaining = MaxNodes;
        return Fits(nodes, 0, ref remaining) ? [.. nodes.Select(WithoutImageLoads)] : null;
    }

    #endregion

    #region Private Methods

    private static bool Fits(IReadOnlyList<MarkdownNode> nodes, int depth, ref int remaining)
    {
        remaining -= nodes.Count;
        if (remaining < 0 || depth > MaxDepth)
        {
            return false;
        }

        foreach (var node in nodes)
        {
            var children = node switch
            {
                ElementNode element => element.Children,
                LinkNode link => link.Children,
                _ => null,
            };
            if (children is not null && !Fits(children, depth + 1, ref remaining))
            {
                return false;
            }
        }

        return true;
    }

    private static MarkdownNode WithoutImageLoads(MarkdownNode node) => node switch
    {
        ImageNode image => new LinkNode(image.Source.AbsoluteUri, [new TextNode(image.Source.AbsoluteUri)]),
        ElementNode element => element with { Children = [.. element.Children.Select(WithoutImageLoads)] },
        LinkNode link => link with { Children = [.. link.Children.Select(WithoutImageLoads)] },
        _ => node,
    };

    #endregion
}
