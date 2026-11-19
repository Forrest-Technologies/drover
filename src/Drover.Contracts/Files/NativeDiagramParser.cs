namespace Drover.Contracts.Files;

/// <summary>
/// Parses supported flowchart and directed graph text into a bounded <see cref="NativeDiagram"/>. Active directives,
/// links, external addresses, and raw HTML are refused.
/// </summary>
public static class NativeDiagramParser
{
    #region Private Fields

    private static readonly string[] supportedHeaders = ["flowchart", "graph"];

    private static readonly string[] unsupportedHeaders =
    [
        "sequencediagram", "classdiagram", "statediagram", "statediagram-v2", "erdiagram", "gantt", "pie",
        "journey", "gitgraph", "mindmap", "timeline", "quadrantchart", "xychart-beta", "sankey-beta",
        "block-beta", "c4context",
    ];

    private static readonly string[] supportedDirections = ["TD", "TB", "BT", "LR", "RL"];

    private static readonly string[] forbiddenPrefixes =
    [
        "click ", "click\t", "href ", "call ", "style ", "classdef ", "class ", "linkstyle ", "subgraph ", "end",
    ];

    private static readonly string[] forbiddenTokens =
    [
        "http://", "https://", "javascript:", "data:", "vbscript:", "file:", "<script", "<img", "<svg",
        "<iframe", "<a ", "url(", "href=",
    ];

    private static readonly string[] plainArrows = ["-.->", "==>", "-->", "---", "->"];

    #endregion

    #region Public Methods

    public static bool IsDiagramLanguage(string? language) =>
        language is not null
        && (language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)
            || language.Equals("flowchart", StringComparison.OrdinalIgnoreCase)
            || language.Equals("diagram", StringComparison.OrdinalIgnoreCase));

    public static bool IsDiagramText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (TryUnwrapSingleFence(trimmed, out var language, out _))
        {
            return IsDiagramLanguage(language);
        }

        foreach (var raw in trimmed.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("%%{", StringComparison.Ordinal))
            {
                return true;
            }

            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            var header = FirstWord(line.TrimEnd(';'));
            return ContainsIgnoreCase(supportedHeaders, header) || ContainsIgnoreCase(unsupportedHeaders, header);
        }

        return false;
    }

    public static bool ValidateMarkdownFences(string markdown, out NativePreviewRefusal? refusal)
    {
        refusal = null;
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var index = 0;

        while (index < lines.Length)
        {
            var trimmed = lines[index].Trim();
            if (!TryReadFenceLanguage(trimmed, out var fence, out var language))
            {
                index++;
                continue;
            }

            index++;
            var body = new StringBuilder();
            var closed = false;
            while (index < lines.Length)
            {
                if (lines[index].Trim().StartsWith(fence, StringComparison.Ordinal))
                {
                    closed = true;
                    index++;
                    break;
                }

                body.Append(lines[index]).Append('\n');
                index++;
            }

            if (IsDiagramLanguage(language) && (!closed || !TryParse(body.ToString(), out _, out refusal)))
            {
                refusal ??= NativePreviewRefusal.Malformed;
                return false;
            }
        }

        return true;
    }

    public static bool TryParse(string text, out NativeDiagram? diagram, out NativePreviewRefusal? refusal)
    {
        diagram = null;
        refusal = null;

        if (Encoding.UTF8.GetByteCount(text) > NativeFilePreview.MaxTextBytes)
        {
            refusal = NativePreviewRefusal.TooLarge;
            return false;
        }

        var trimmed = text.Trim();
        if (TryUnwrapSingleFence(trimmed, out var language, out var unwrapped))
        {
            if (!IsDiagramLanguage(language))
            {
                refusal = NativePreviewRefusal.Unsupported;
                return false;
            }

            trimmed = unwrapped.Trim();
        }

        if (!TryReadLines(trimmed, out var lines, out refusal))
        {
            return false;
        }

        if (!TryReadHeader(lines[0], out var direction, out refusal))
        {
            return false;
        }

        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        List<string> order = [];
        List<NativeDiagramEdge> edges = [];

        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            foreach (var part in SplitStatements(lines[lineIndex]))
            {
                if (!TryParseStatement(part, nodes, order, edges, out refusal))
                {
                    return false;
                }
            }
        }

        if (order.Count == 0)
        {
            refusal = NativePreviewRefusal.Malformed;
            return false;
        }

        List<NativeDiagramNode> builtNodes = [];
        foreach (var id in order)
        {
            builtNodes.Add(new(id, nodes[id]));
        }

        diagram = new(direction, builtNodes, edges);
        return true;
    }

    #endregion

    #region Private Methods

    private static bool TryReadLines(string text, out List<string> lines, out NativePreviewRefusal? refusal)
    {
        lines = [];
        refusal = null;

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("%%{", StringComparison.Ordinal))
            {
                refusal = NativePreviewRefusal.Malformed;
                return false;
            }

            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(line);
        }

        if (lines.Count == 0)
        {
            refusal = NativePreviewRefusal.Malformed;
            return false;
        }

        return true;
    }

    private static bool TryReadHeader(string headerLine, out string direction, out NativePreviewRefusal? refusal)
    {
        direction = "TD";
        refusal = null;
        var clean = headerLine.TrimEnd(';').Trim();
        var parts = clean.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            refusal = NativePreviewRefusal.Malformed;
            return false;
        }

        if (ContainsIgnoreCase(unsupportedHeaders, parts[0]))
        {
            refusal = NativePreviewRefusal.Unsupported;
            return false;
        }

        if (!ContainsIgnoreCase(supportedHeaders, parts[0]))
        {
            refusal = NativePreviewRefusal.Unsupported;
            return false;
        }

        if (parts.Length > 2)
        {
            refusal = NativePreviewRefusal.Malformed;
            return false;
        }

        if (parts.Length == 2)
        {
            var candidate = parts[1].ToUpperInvariant();
            if (!ContainsIgnoreCase(supportedDirections, candidate))
            {
                refusal = NativePreviewRefusal.Malformed;
                return false;
            }

            direction = candidate == "TB" ? "TD" : candidate;
        }

        return true;
    }

    private static bool TryParseStatement(
        string statement,
        Dictionary<string, string> nodes,
        List<string> order,
        List<NativeDiagramEdge> edges,
        out NativePreviewRefusal? refusal)
    {
        refusal = null;
        var clean = statement.Trim();
        if (clean.Length == 0)
        {
            return true;
        }

        if (IsForbiddenStatement(clean))
        {
            refusal = NativePreviewRefusal.Malformed;
            return false;
        }

        if (TrySplitEdge(clean, out var left, out var right, out var edgeLabel, out refusal))
        {
            if (refusal is not null
                || !TryParseNode(left, nodes, order, out var fromId, out refusal)
                || !TryParseNode(right, nodes, order, out var toId, out refusal))
            {
                return false;
            }

            edges.Add(new(fromId, toId, edgeLabel));
            if (edges.Count > NativeFilePreview.MaxDiagramEdges)
            {
                refusal = NativePreviewRefusal.TooComplex;
                return false;
            }

            return true;
        }

        return refusal is null && TryParseNode(clean, nodes, order, out _, out refusal);
    }

    private static bool TrySplitEdge(
        string statement,
        out string left,
        out string right,
        out string? label,
        out NativePreviewRefusal? refusal)
    {
        left = string.Empty;
        right = string.Empty;
        label = null;
        refusal = null;

        foreach (var arrow in plainArrows)
        {
            var index = FindOutsideBrackets(statement, arrow);
            if (index <= 0)
            {
                continue;
            }

            left = statement[..index].Trim();
            var remainder = statement[(index + arrow.Length)..].Trim();
            if (remainder.StartsWith('|'))
            {
                var endPipe = remainder.IndexOf('|', 1);
                if (endPipe < 0)
                {
                    refusal = NativePreviewRefusal.Malformed;
                    return false;
                }

                label = CleanLabel(remainder[1..endPipe], out refusal);
                if (refusal is not null)
                {
                    return false;
                }

                remainder = remainder[(endPipe + 1)..].Trim();
            }

            if (remainder.Length == 0)
            {
                refusal = NativePreviewRefusal.Malformed;
                return false;
            }

            right = remainder;
            return true;
        }

        var inlineLabelStart = FindOutsideBrackets(statement, "-- ");
        if (inlineLabelStart > 0)
        {
            var afterStart = statement[(inlineLabelStart + 3)..];
            var inlineArrow = FindOutsideBrackets(afterStart, "-->");
            if (inlineArrow > 0)
            {
                left = statement[..inlineLabelStart].Trim();
                label = CleanLabel(afterStart[..inlineArrow], out refusal);
                right = afterStart[(inlineArrow + 3)..].Trim();
                if (refusal is not null || right.Length == 0)
                {
                    refusal ??= NativePreviewRefusal.Malformed;
                    return false;
                }

                return true;
            }
        }

        return false;
    }

    private static bool TryParseNode(
        string token,
        Dictionary<string, string> nodes,
        List<string> order,
        out string id,
        out NativePreviewRefusal? refusal)
    {
        id = string.Empty;
        refusal = null;
        var clean = token.Trim();
        var idLength = 0;
        while (idLength < clean.Length && IsIdentifierChar(clean[idLength]))
        {
            idLength++;
        }

        if (idLength == 0)
        {
            refusal = NativePreviewRefusal.Malformed;
            return false;
        }

        id = clean[..idLength];
        if (id.Length > NativeFilePreview.MaxDiagramLabelLength)
        {
            refusal = NativePreviewRefusal.TooComplex;
            return false;
        }

        var rest = clean[idLength..].Trim();
        string? label = null;
        if (rest.Length > 0)
        {
            if (!TryUnwrapShape(rest, out var rawLabel))
            {
                refusal = NativePreviewRefusal.Malformed;
                return false;
            }

            label = CleanLabel(rawLabel, out refusal);
            if (refusal is not null || string.IsNullOrWhiteSpace(label))
            {
                refusal ??= NativePreviewRefusal.Malformed;
                return false;
            }
        }

        if (!nodes.ContainsKey(id))
        {
            if (nodes.Count >= NativeFilePreview.MaxDiagramNodes)
            {
                refusal = NativePreviewRefusal.TooComplex;
                return false;
            }

            order.Add(id);
            nodes[id] = label ?? id;
        }
        else if (label is not null)
        {
            nodes[id] = label;
        }

        return true;
    }

    private static bool TryUnwrapShape(string shape, out string inner)
    {
        inner = string.Empty;
        (string Open, string Close)[] pairs =
        [
            ("((", "))"),
            ("[[", "]]"),
            ("[", "]"),
            ("(", ")"),
            ("{", "}"),
        ];

        foreach (var (open, close) in pairs)
        {
            if (shape.StartsWith(open, StringComparison.Ordinal)
                && shape.EndsWith(close, StringComparison.Ordinal)
                && shape.Length > open.Length + close.Length)
            {
                inner = shape[open.Length..^close.Length].Trim();
                if (inner.Length >= 2 && inner.StartsWith('"') && inner.EndsWith('"'))
                {
                    inner = inner[1..^1].Trim();
                }

                return true;
            }
        }

        return false;
    }

    private static string? CleanLabel(string raw, out NativePreviewRefusal? refusal)
    {
        refusal = null;
        var clean = raw.Trim();
        if (clean.Length >= 2 && clean.StartsWith('"') && clean.EndsWith('"'))
        {
            clean = clean[1..^1].Trim();
        }

        if (clean.Length > NativeFilePreview.MaxDiagramLabelLength)
        {
            refusal = NativePreviewRefusal.TooComplex;
            return null;
        }

        if (clean.Contains('<') || clean.Contains('>') || ContainsForbiddenToken(clean))
        {
            refusal = NativePreviewRefusal.Malformed;
            return null;
        }

        return clean.Length == 0 ? null : clean;
    }

    private static bool IsForbiddenStatement(string statement)
    {
        foreach (var prefix in forbiddenPrefixes)
        {
            if (statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || statement.Equals(prefix.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ContainsForbiddenToken(statement);
    }

    private static bool ContainsForbiddenToken(string text)
    {
        foreach (var token in forbiddenTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindOutsideBrackets(string text, string target)
    {
        var depth = 0;
        var inQuotes = false;

        for (var index = 0; index <= text.Length - target.Length; index++)
        {
            var current = text[index];
            if (current == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (current is '[' or '(' or '{')
                {
                    depth++;
                }
                else if (current is ']' or ')' or '}')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && text.AsSpan(index, target.Length).SequenceEqual(target))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static List<string> SplitStatements(string line)
    {
        List<string> statements = [];
        var depth = 0;
        var inQuotes = false;
        var start = 0;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (current is '[' or '(' or '{')
                {
                    depth++;
                }
                else if (current is ']' or ')' or '}')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && current == ';')
                {
                    statements.Add(line[start..index]);
                    start = index + 1;
                }
            }
        }

        if (start < line.Length)
        {
            statements.Add(line[start..]);
        }

        return statements;
    }

    private static bool TryUnwrapSingleFence(string text, out string language, out string body)
    {
        language = string.Empty;
        body = string.Empty;
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 2 || !TryReadFenceLanguage(lines[0].Trim(), out var fence, out language))
        {
            return false;
        }

        if (lines[^1].Trim() != fence)
        {
            return false;
        }

        for (var index = 1; index < lines.Length - 1; index++)
        {
            if (lines[index].Trim().StartsWith(fence, StringComparison.Ordinal))
            {
                return false;
            }
        }

        body = string.Join('\n', lines[1..^1]);
        return true;
    }

    private static bool TryReadFenceLanguage(string line, out string fence, out string language)
    {
        fence = string.Empty;
        language = string.Empty;
        if (!line.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        var ticks = 0;
        while (ticks < line.Length && line[ticks] == '`')
        {
            ticks++;
        }

        fence = line[..ticks];
        language = line[ticks..].Trim();
        return !language.Contains('`');
    }

    private static string FirstWord(string line)
    {
        var index = 0;
        while (index < line.Length && !char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return line[..index];
    }

    private static bool IsIdentifierChar(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '.';

    private static bool ContainsIgnoreCase(string[] items, string candidate)
    {
        foreach (var item in items)
        {
            if (item.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
