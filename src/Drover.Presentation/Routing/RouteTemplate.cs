namespace Drover.Presentation.Routing;

/// <summary>
/// One address pattern, written the way the screens always wrote theirs: literal segments and named parameters,
/// a parameter optionally held to an id with <c>:guid</c>. "/talk/{BotId:guid}/side/{SideChat}" is one.
/// </summary>
public sealed class RouteTemplate(string pattern)
{
    #region Private Fields

    private const string GuidConstraint = "guid";

    private readonly Segment[] segments = [.. Split(pattern).Select(Parse)];

    #endregion

    #region Properties

    public string Pattern { get; } = pattern;

    /// <summary>
    /// How exact the pattern is, segment by segment: a literal before an id, an id before any text. Of two patterns
    /// that fit an address, the one that sorts first is the one meant.
    /// </summary>
    public string Precedence => string.Concat(segments.Select(segment => segment.Rank));

    #endregion

    #region Public Methods

    /// <summary>The parameters an address path gives this pattern, or null when the path does not fit it.</summary>
    public IReadOnlyDictionary<string, string>? Match(string path)
    {
        var parts = Split(path);
        if (parts.Length != segments.Length)
        {
            return null;
        }

        Dictionary<string, string> parameters = [with(StringComparer.OrdinalIgnoreCase)];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = Uri.UnescapeDataString(parts[index]);
            if (!segments[index].Takes(part))
            {
                return null;
            }

            if (segments[index].Parameter is { } name)
            {
                parameters[name] = part;
            }
        }

        return parameters;
    }

    #endregion

    #region Private Methods

    private static string[] Split(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static Segment Parse(string text)
    {
        if (!text.StartsWith('{') || !text.EndsWith('}'))
        {
            return new(text, null, false);
        }

        var inner = text[1..^1].Split(':', 2);
        if (inner.Length == 2 && inner[1] != GuidConstraint)
        {
            throw new ArgumentException($"The address pattern holds '{inner[1]}', and only '{GuidConstraint}' is known.");
        }

        return new(null, inner[0], inner.Length == 2);
    }

    private sealed record Segment(string? Literal, string? Parameter, bool IsGuid)
    {
        public char Rank => Literal is not null ? '0' : IsGuid ? '1' : '2';

        public bool Takes(string part) => Literal is not null
            ? string.Equals(Literal, part, StringComparison.OrdinalIgnoreCase)
            : !IsGuid || Guid.TryParse(part, out _);
    }

    #endregion
}
