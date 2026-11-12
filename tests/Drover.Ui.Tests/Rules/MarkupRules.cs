namespace Drover.Ui.Tests.Rules;

/// <summary>
/// The DESIGN.md rules that are about markup. Each takes one parsed Razor file and returns what breaks.
/// </summary>
public static class MarkupRules
{
    #region Private Fields

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex hypeWord = new(
        @"(?<![\w-])(?:powerful|seamless|supercharg|magic|next-gen|ai-powered)\w*",
        Options);

    /// <summary>
    /// An exclamation mark that ends a word or a sentence. C# uses of the character (not, not-equal,
    /// null-forgiving) are followed by a name, an equals sign, or punctuation, so they do not match.
    /// </summary>
    private static readonly Regex exclamation = new(
        """(?<=[\p{L}\p{N}.?!'"’])!(?=\s|$|[<"'!?])""",
        Options | RegexOptions.Multiline);

    private static readonly Regex implicitExpression = new(@"@[\w.]+", Options);

    private static readonly HashSet<string> textAttributes =
    [
        "label", "title", "placeholder", "alt", "aria-label", "aria-description", "aria-placeholder",
        "aria-valuetext", "aria-roledescription", "text", "description", "heading", "subtitle", "message",
        "hint", "caption", "summary", "tooltip", "help", "error",
    ];

    private static readonly HashSet<string> colorAttributes =
    [
        "fill", "stroke", "color", "stop-color", "flood-color", "lighting-color",
    ];

    private static readonly string[] namedByValue = ["hidden", "submit", "reset", "button"];

    #endregion

    #region Public Methods

    /// <summary>
    /// A click handler on a <c>div</c> or a <c>span</c>, which has no role, no focus, and no keyboard path.
    /// </summary>
    public static IEnumerable<Violation> ControlsAreRealControls(string file, MarkupDocument document) =>
        from tag in document.Tags
        where tag.Is("div", "span") && tag.Has("@onclick")
        select new Violation(
            RuleNames.ControlsAreRealControls,
            file,
            tag.Line,
            $"a <{tag.Name}> handles a click; use a button for an action or an a for navigation");

    /// <summary>
    /// An <c>IconButton</c> with no <c>Label</c>, or an empty one. Splatted attributes do not count.
    /// </summary>
    public static IEnumerable<Violation> IconOnlyControlsAreNamed(string file, MarkupDocument document) =>
        from tag in document.Tags
        where tag.Is("IconButton") && !tag.IsClosing && !tag.HasText("Label")
        select new Violation(
            RuleNames.IconOnlyControlsAreNamed,
            file,
            tag.Line,
            "an IconButton has no Label; the label is its accessible name");

    /// <summary>
    /// An <c>input</c>, <c>select</c>, or <c>textarea</c> with no label: not inside a <c>label</c>, no
    /// <c>label for</c> with its id in the same file, and no <c>aria-label</c> or <c>aria-labelledby</c>.
    /// Inputs of type hidden, submit, reset, and button are not fields.
    /// </summary>
    public static IEnumerable<Violation> FieldsAreLabeled(string file, MarkupDocument document)
    {
        HashSet<string> labeledIds =
            [.. document.Tags.Where(tag => tag.Is("label")).Select(tag => tag.Value("for")).OfType<string>()];
        var openLabels = 0;
        foreach (var tag in document.Tags)
        {
            if (tag.Is("label"))
            {
                openLabels += tag.IsClosing ? -1 : tag.IsSelfClosing ? 0 : 1;
            }
            else if (IsField(tag) && openLabels <= 0 && !IsNamed(tag, labeledIds))
            {
                yield return new(
                    RuleNames.FieldsAreLabeled,
                    file,
                    tag.Line,
                    $"a <{tag.Name}> has no label; wrap it in a label, point a label at its id, or set aria-label");
            }
        }
    }

    /// <summary>
    /// An image or embed with no <c>width</c> and <c>height</c>, which moves the page when it loads.
    /// </summary>
    public static IEnumerable<Violation> MediaHasDimensions(string file, MarkupDocument document) =>
        from tag in document.Tags
        where tag.Is("img", "iframe", "video", "embed") && !tag.IsClosing && !(tag.Has("width") && tag.Has("height"))
        select new Violation(
            RuleNames.MediaHasDimensions,
            file,
            tag.Line,
            $"an <{tag.Name}> needs both width and height so nothing jumps while it loads");

    /// <summary>
    /// A hype word or an exclamation mark in interface text: text content and the attributes that hold text.
    /// </summary>
    public static IEnumerable<Violation> NoHypeWords(string file, MarkupDocument document)
    {
        var inText =
            from found in Hype(document.Text)
            select (Line: SourceText.LineAt(document.Text, found.Index), found.Message);
        var inAttributes =
            from tag in document.Tags
            from attribute in tag.Attributes
            where textAttributes.Contains(attribute.Name.ToLowerInvariant())
            from found in Hype(Literal(attribute.Value))
            select (Line: attribute.Line + attribute.Value.AsSpan(0, found.Index).Count('\n'), found.Message);
        return inText.Concat(inAttributes)
            .Select(found => new Violation(RuleNames.NoHypeWords, file, found.Line, found.Message));
    }

    /// <summary>
    /// A raw color in an SVG presentation attribute. Geometry attributes are not CSS values and are not checked.
    /// </summary>
    public static IEnumerable<Violation> NoRawColors(string file, MarkupDocument document) =>
        from tag in document.Tags
        from attribute in tag.Attributes
        where colorAttributes.Contains(attribute.Name.ToLowerInvariant())
        where StyleRules.IsRawColor(Literal(attribute.Value))
        select new Violation(
            RuleNames.NoRawValues,
            file,
            attribute.Line,
            $"'{attribute.Value}' is a raw color in '{attribute.Name}'; use currentColor or a token from tokens.css");

    #endregion

    #region Private Methods

    private static bool IsField(MarkupTag tag) =>
        !tag.IsClosing
        && tag.Is("input", "select", "textarea")
        && !(tag.Is("input") && namedByValue.Contains(tag.Value("type") ?? string.Empty));

    private static bool IsNamed(MarkupTag tag, HashSet<string> labeledIds) =>
        tag.HasText("aria-label")
        || tag.HasText("aria-labelledby")
        || (tag.Value("id") is { } id && labeledIds.Contains(id));

    private static string Literal(string value) =>
        SourceText.BlankMatches(Markup.MaskExpressions(value), implicitExpression);

    private static IEnumerable<(int Index, string Message)> Hype(string text) =>
        hypeWord.Matches(text)
            .Select(match => (match.Index, $"'{match.Value}' is hype; say what the thing does"))
            .Concat(exclamation.Matches(text).Select(match => (match.Index, "interface text has an exclamation mark")));

    #endregion
}
