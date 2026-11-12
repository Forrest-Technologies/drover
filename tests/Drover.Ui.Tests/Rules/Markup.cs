namespace Drover.Ui.Tests.Rules;

/// <summary>
/// Reads a Razor file into tags, text, and styles. Comments, directive lines, and <c>@code</c> blocks are
/// masked first, so nothing in them is read as markup. It is a reader for rule checks, not a Razor parser.
/// </summary>
public static class Markup
{
    #region Private Fields

    private static readonly Regex razorComment = new(@"@\*.*?\*@", RegexOptions.Singleline);

    private static readonly Regex htmlComment = new("<!--.*?-->", RegexOptions.Singleline);

    private static readonly Regex directiveLine = new(
        @"^[ \t]*@(?:page|using|inject|inherits|implements|attribute|typeparam|layout|namespace|rendermode)\b[^\n]*",
        RegexOptions.Multiline);

    private static readonly Regex codeBlock = new(@"@(?:code|functions)\s*\{");

    private static readonly Regex statementBlock = new(@"@\{");

    private static readonly Regex expression = new(@"@\(");

    #endregion

    #region Public Methods

    public static MarkupDocument Parse(string text)
    {
        var source = MaskCode(text);
        var content = source;
        var styles = SourceText.Blanked(source);
        List<MarkupTag> tags = [];
        var index = NextTag(source, 0);
        while (index >= 0)
        {
            var (tag, end) = ReadTag(source, index);
            tags.Add(tag);
            if (!tag.IsClosing && !tag.IsSelfClosing && tag.Is("style", "script"))
            {
                var close = source.IndexOf($"</{tag.Name}", end, StringComparison.OrdinalIgnoreCase);
                var bodyEnd = close < 0 ? source.Length : close;
                styles = tag.Is("style") ? $"{styles[..end]}{source[end..bodyEnd]}{styles[bodyEnd..]}" : styles;
                end = bodyEnd;
            }

            content = SourceText.BlankRange(content, index, end);
            index = NextTag(source, end);
        }

        return new(tags, MaskExpressions(content), [.. StyleSheet.Parse(styles), .. InlineStyles(source, tags)]);
    }

    /// <summary>
    /// The literal part of an attribute value or a run of text: Razor expressions are masked.
    /// </summary>
    public static string MaskExpressions(string text) =>
        SourceText.BlankBalanced(SourceText.BlankBalanced(text, expression, '(', ')'), statementBlock, '{', '}');

    #endregion

    #region Masking

    private static string MaskCode(string text)
    {
        var masked = SourceText.BlankMatches(text, razorComment);
        masked = SourceText.BlankMatches(masked, htmlComment);
        masked = SourceText.BlankMatches(masked, directiveLine);
        return SourceText.BlankBalanced(masked, codeBlock, '{', '}');
    }

    private static IEnumerable<StyleRule> InlineStyles(string source, IEnumerable<MarkupTag> tags) =>
        from tag in tags
        from attribute in tag.Attributes
        where string.Equals(attribute.Name, "style", StringComparison.OrdinalIgnoreCase)
        from rule in StyleSheet.Parse($"{SourceText.Blanked(source[..attribute.ValueOffset])}{attribute.Value}")
        select rule;

    #endregion

    #region Tags

    private static int NextTag(string source, int from)
    {
        var index = source.IndexOf('<', from);
        while (index >= 0 && !StartsTag(source, index))
        {
            index = source.IndexOf('<', index + 1);
        }

        return index;
    }

    private static bool StartsTag(string source, int index)
    {
        var next = index + 1 < source.Length && source[index + 1] is '/' ? index + 2 : index + 1;
        return next < source.Length && char.IsAsciiLetter(source[next]);
    }

    private static (MarkupTag Tag, int End) ReadTag(string source, int start)
    {
        var isClosing = source[start + 1] is '/';
        var nameStart = isClosing ? start + 2 : start + 1;
        var position = nameStart;
        while (position < source.Length && IsNameCharacter(source[position]))
        {
            position++;
        }

        var name = source[nameStart..position];
        List<AttributeNode> attributes = [];
        var isSelfClosing = false;
        while (position < source.Length && source[position] is not '>')
        {
            if (char.IsWhiteSpace(source[position]) || source[position] is '/' or '=')
            {
                isSelfClosing |= source[position] is '/';
                position++;
                continue;
            }

            (var attribute, position) = ReadAttribute(source, position);
            attributes.Add(attribute);
            isSelfClosing = false;
        }

        var tag = new MarkupTag(name, isClosing, isSelfClosing, attributes, SourceText.LineAt(source, start));
        return (tag, Math.Min(position + 1, source.Length));
    }

    private static (AttributeNode Attribute, int End) ReadAttribute(string source, int start)
    {
        var position = start;
        while (position < source.Length && !EndsAttributeName(source[position]))
        {
            position = source[position] is '(' ? SourceText.SkipBalanced(source, position, '(', ')') : position + 1;
        }

        var name = source[start..position];
        var line = SourceText.LineAt(source, start);
        var afterName = SkipWhiteSpace(source, position);
        if (afterName >= source.Length || source[afterName] is not '=')
        {
            return (new(name, string.Empty, line, position), position);
        }

        var valueStart = SkipWhiteSpace(source, afterName + 1);
        return valueStart < source.Length && source[valueStart] is '"' or '\''
            ? ReadQuotedValue(source, name, line, valueStart)
            : ReadBareValue(source, name, line, valueStart);
    }

    private static (AttributeNode Attribute, int End) ReadQuotedValue(
        string source,
        string name,
        int line,
        int quoteIndex)
    {
        var quote = source[quoteIndex];
        var position = quoteIndex + 1;
        while (position < source.Length && source[position] != quote)
        {
            position = StartsExpression(source, position)
                ? SourceText.SkipBalanced(source, position + 1, '(', ')')
                : position + 1;
        }

        var end = Math.Min(position, source.Length);
        return (new(name, source[(quoteIndex + 1)..end], line, quoteIndex + 1), Math.Min(end + 1, source.Length));
    }

    private static (AttributeNode Attribute, int End) ReadBareValue(string source, string name, int line, int start)
    {
        var position = start;
        while (position < source.Length && !char.IsWhiteSpace(source[position]) && source[position] is not '>')
        {
            position = source[position] is '(' ? SourceText.SkipBalanced(source, position, '(', ')') : position + 1;
        }

        return (new(name, source[start..position], line, start), position);
    }

    private static bool IsNameCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':';

    private static bool EndsAttributeName(char character) =>
        char.IsWhiteSpace(character) || character is '=' or '>' or '/';

    private static bool StartsExpression(string source, int index) =>
        source[index] is '@' && index + 1 < source.Length && source[index + 1] is '(';

    private static int SkipWhiteSpace(string source, int start)
    {
        var position = start;
        while (position < source.Length && char.IsWhiteSpace(source[position]))
        {
            position++;
        }

        return position;
    }

    #endregion
}
