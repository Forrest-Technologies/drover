namespace Drover.Ui.Markdown;

/// <summary>
/// Turns message text into nodes that are safe to render. Three layers, each enough on its own for the common
/// attacks: the parser has raw HTML switched off, its output passes through a sanitizer with a short allowlist,
/// and only the node kinds in this namespace come out, each built from validated values.
/// </summary>
public static class MarkdownDocument
{
    #region Private Fields

    private const string LanguagePrefix = "language-";

    private static readonly HashSet<string> allowedTags =
    [
        "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "em", "del", "blockquote", "ul", "ol", "li",
        "code", "pre", "a", "img", "input", "table", "thead", "tbody", "tr", "th", "td",
    ];

    private static readonly HashSet<string> allowedAttributes =
        ["href", "src", "alt", "class", "start", "type", "checked"];

    private static readonly HashSet<string> uriAttributes = ["href", "src"];

    private static readonly HashSet<string> linkSchemes = ["http", "https", "mailto"];

    private static readonly HashSet<string> imageSchemes = ["http", "https"];

    /// <summary>Elements passed through by name. The other allowed tags become their own node kinds.</summary>
    private static readonly HashSet<string> plainElements =
    [
        "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "em", "del", "blockquote", "ul", "li",
        "code", "table", "thead", "tbody", "tr", "th", "td",
    ];

    private static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    private static readonly HtmlSanitizer sanitizer = new(new HtmlSanitizerOptions
    {
        AllowedTags = allowedTags,
        AllowedAttributes = allowedAttributes,
        AllowedSchemes = linkSchemes,
        UriAttributes = uriAttributes,
    });

    #endregion

    #region Public Methods

    public static IReadOnlyList<MarkdownNode> Parse(string text)
    {
        var html = Markdig.Markdown.ToHtml(text, pipeline);
        using var document = sanitizer.SanitizeDom(html);
        return document.Body is { } body ? ConvertChildren(body) : [];
    }

    #endregion

    #region Conversion

    private static List<MarkdownNode> ConvertChildren(INode parent) =>
        [.. parent.ChildNodes.SelectMany(Convert)];

    private static IEnumerable<MarkdownNode> Convert(INode node) => node switch
    {
        IText text => [new TextNode(text.Data)],
        IElement element => ConvertElement(element),
        _ => [],
    };

    private static IEnumerable<MarkdownNode> ConvertElement(IElement element) => element.LocalName switch
    {
        "pre" => [ConvertCode(element)],
        "img" => ConvertImage(element),
        "a" => ConvertLink(element),
        "input" => ConvertTask(element),
        "ol" => [new ElementNode("ol", ConvertChildren(element), ListStart(element))],
        var name when plainElements.Contains(name) => [new ElementNode(name, ConvertChildren(element))],
        _ => ConvertChildren(element),
    };

    private static CodeNode ConvertCode(IElement pre)
    {
        var language = pre.FirstElementChild?.ClassList
            .FirstOrDefault(name => name.StartsWith(LanguagePrefix, StringComparison.Ordinal))?[LanguagePrefix.Length..];
        var code = pre.TextContent;

        return new(code.EndsWith('\n') ? code[..^1] : code, string.IsNullOrWhiteSpace(language) ? null : language);
    }

    private static IEnumerable<MarkdownNode> ConvertImage(IElement image)
    {
        var alt = image.GetAttribute("alt") ?? string.Empty;

        if (AbsoluteAddress(image.GetAttribute("src"), imageSchemes) is { } source)
        {
            return [new ImageNode(source, alt)];
        }

        return alt.Length > 0 ? [new TextNode(alt)] : [];
    }

    /// <summary>
    /// A link around an image would put one control inside another, so the image stands alone and the address
    /// follows it as its own link.
    /// </summary>
    private static List<MarkdownNode> ConvertLink(IElement link)
    {
        var children = ConvertChildren(link);

        if (AbsoluteAddress(link.GetAttribute("href"), linkSchemes) is not { } address)
        {
            return children;
        }

        return link.QuerySelector("img") is null
            ? [new LinkNode(address.AbsoluteUri, children)]
            : [.. children, new TextNode(" "), new LinkNode(address.AbsoluteUri, [new TextNode(address.AbsoluteUri)])];
    }

    private static IEnumerable<MarkdownNode> ConvertTask(IElement input) =>
        input.GetAttribute("type") == "checkbox" ? [new TaskNode(input.HasAttribute("checked"))] : [];

    private static int? ListStart(IElement list) =>
        int.TryParse(list.GetAttribute("start"), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            ? start
            : null;

    private static Uri? AbsoluteAddress(string? value, HashSet<string> schemes) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address) && schemes.Contains(address.Scheme) ? address : null;

    #endregion
}
