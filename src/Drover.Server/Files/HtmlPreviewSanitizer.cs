namespace Drover.Server.Files;

/// <summary>Reduces untrusted HTML to static document structure with no address-bearing content.</summary>
public static class HtmlPreviewSanitizer
{
    #region Private Fields

    private static readonly string[] imagePrefixes =
    [
        "data:image/gif;base64,", "data:image/jpeg;base64,", "data:image/png;base64,", "data:image/webp;base64,",
    ];

    private static readonly HashSet<string> tags =
    [
        "html", "head", "body", "title", "main", "header", "footer", "section", "article", "aside", "nav", "div",
        "span", "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "em", "b", "i", "u", "s",
        "small", "sub", "sup", "blockquote", "pre", "code", "ol", "ul", "li", "dl", "dt", "dd", "table",
        "caption", "thead", "tbody", "tfoot", "tr", "th", "td", "colgroup", "col", "figure", "figcaption", "img",
    ];

    private static readonly HashSet<string> attributes =
        ["alt", "colspan", "height", "rowspan", "span", "src", "style", "title", "width"];

    private static readonly HashSet<string> css =
    [
        "background-color", "border", "border-bottom", "border-collapse", "border-color", "border-left", "border-radius",
        "border-right", "border-style", "border-top", "border-width", "color", "display", "font-family", "font-size",
        "font-style", "font-weight", "height", "letter-spacing", "line-height", "list-style-type", "margin", "margin-bottom",
        "margin-left", "margin-right", "margin-top", "max-height", "max-width", "min-height", "min-width", "overflow",
        "padding", "padding-bottom", "padding-left", "padding-right", "padding-top", "text-align", "text-decoration",
        "text-indent", "text-transform", "vertical-align", "white-space", "width", "word-break", "word-spacing",
    ];

    private static readonly Ganss.Xss.HtmlSanitizer sanitizer = Build();

    #endregion

    #region Public Methods

    public static string Clean(string html)
    {
        using var document = sanitizer.SanitizeDom(html);
        foreach (var image in document.QuerySelectorAll("img[src]"))
        {
            var source = image.GetAttribute("src");
            if (source is null || !imagePrefixes.Any(prefix => source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                image.RemoveAttribute("src");
            }
        }

        return document.DocumentElement?.OuterHtml ?? string.Empty;
    }

    #endregion

    #region Private Methods

    private static Ganss.Xss.HtmlSanitizer Build()
    {
        var options = Ganss.Xss.HtmlSanitizerOptions.Empty();
        options.AllowedTags.UnionWith(tags);
        options.AllowedAttributes.UnionWith(attributes);
        options.AllowedCssProperties.UnionWith(css);
        options.AllowedSchemes.Add("data");
        options.UriAttributes.Add("src");
        return new Ganss.Xss.HtmlSanitizer(options) { KeepChildNodes = true };
    }

    #endregion
}
