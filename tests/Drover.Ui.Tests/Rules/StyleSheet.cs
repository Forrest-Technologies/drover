namespace Drover.Ui.Tests.Rules;

/// <summary>
/// Reads CSS into blocks and declarations. It is a reader for rule checks, not a validating parser.
/// </summary>
public static class StyleSheet
{
    #region Public Methods

    public static IReadOnlyList<StyleRule> Parse(string text)
    {
        var source = MaskComments(text);
        List<StyleRule> rules = [];
        List<OpenBlock> open = [new(string.Empty, 1)];
        var segmentStart = 0;
        var index = 0;
        while (index < source.Length)
        {
            var character = source[index];
            if (character is '"' or '\'')
            {
                index = SourceText.SkipString(source, index);
                continue;
            }

            if (character is '(')
            {
                index = SourceText.SkipBalanced(source, index, '(', ')');
                continue;
            }

            if (character is '{')
            {
                open.Add(new(source[segmentStart..index].Trim(), LineOfContent(source, segmentStart)));
                segmentStart = index + 1;
            }
            else if (character is ';' or '}')
            {
                AddDeclaration(open[^1], source, segmentStart, index);
                segmentStart = index + 1;
                if (character is '}' && open.Count > 1)
                {
                    rules.Add(Close(open));
                }
            }

            index++;
        }

        AddDeclaration(open[0], source, segmentStart, source.Length);
        if (open[0].Declarations.Count > 0)
        {
            rules.Add(new(string.Empty, [], open[0].Declarations, 1));
        }

        return rules;
    }

    #endregion

    #region Private Methods

    private static StyleRule Close(List<OpenBlock> open)
    {
        var block = open[^1];
        open.RemoveAt(open.Count - 1);
        return new(block.Prelude, [.. open.Skip(1).Select(outer => outer.Prelude)], block.Declarations, block.Line);
    }

    private static void AddDeclaration(OpenBlock block, string source, int start, int end)
    {
        var segment = source[start..end];
        var colon = segment.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || string.IsNullOrWhiteSpace(segment[..colon]))
        {
            return;
        }

        var property = segment[..colon].Trim().ToLowerInvariant();
        var valueStart = start + colon + 1;
        block.Declarations.Add(new(
            property,
            source[valueStart..end],
            LineOfContent(source, start),
            SourceText.LineAt(source, valueStart)));
    }

    private static int LineOfContent(string source, int start)
    {
        var index = start;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return SourceText.LineAt(source, index);
    }

    private static string MaskComments(string text)
    {
        var masked = text;
        var index = 0;
        while (index < masked.Length)
        {
            if (masked[index] is '"' or '\'')
            {
                index = SourceText.SkipString(masked, index);
            }
            else if (masked[index] is '/' && index + 1 < masked.Length && masked[index + 1] is '*')
            {
                var close = masked.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = close < 0 ? masked.Length : close + 2;
                masked = SourceText.BlankRange(masked, index, end);
                index = end;
            }
            else
            {
                index++;
            }
        }

        return masked;
    }

    #endregion

    #region Open Blocks

    private sealed record OpenBlock(string Prelude, int Line)
    {
        public List<StyleDeclaration> Declarations { get; } = [];
    }

    #endregion
}
