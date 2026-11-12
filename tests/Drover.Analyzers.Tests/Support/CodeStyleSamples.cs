namespace Drover.Analyzers.Tests.Support;

/// <summary>Pulls the C# samples out of CODE_STYLE.md and sorts them into compliant and non-compliant.</summary>
public static partial class CodeStyleSamples
{
    #region Private Fields

    private const string CompliantMarker = "// ✅";

    private const string NonCompliantMarker = "// ❌";

    #endregion

    #region Public Methods

    public static List<string> Compliant() => [.. Segments().Where(segment => segment.IsCompliant).Select(segment => segment.Code)];

    public static List<string> NonCompliant() => [.. Segments().Where(segment => !segment.IsCompliant).Select(segment => segment.Code)];

    /// <summary>The ways a fragment from the guide is turned into something the compiler will parse.</summary>
    public static IEnumerable<string> Wrappings(string fragment)
    {
        var signatures = EndsWithParenthesis().Replace(fragment, ");");

        return
        [
            fragment,
            $"{fragment};",
            $"public class Sample\n{{\n{fragment}\n}}",
            $"public class Sample\n{{\n{fragment};\n}}",
            $"public class Sample\n{{\n{signatures}\n}}",
        ];
    }

    #endregion

    #region Private Methods

    private static IEnumerable<Segment> Segments() =>
        FencedBlock()
            .Matches(RepositoryFiles.Read("CODE_STYLE.md"))
            .SelectMany(match => SplitByMarkers(match.Groups["code"].Value))
            .Where(segment => segment.Code.Length > 0);

    private static IEnumerable<Segment> SplitByMarkers(string block)
    {
        var current = new List<string>();
        var compliant = true;

        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.TrimStart();
            var startsCompliant = trimmed.StartsWith(CompliantMarker, StringComparison.Ordinal);

            if (!startsCompliant && !trimmed.StartsWith(NonCompliantMarker, StringComparison.Ordinal))
            {
                current.Add(line);
                continue;
            }

            yield return new Segment(string.Join("\n", current).Trim(), compliant);
            current = [];
            compliant = startsCompliant;
        }

        yield return new Segment(string.Join("\n", current).Trim(), compliant);
    }

    [GeneratedRegex(@"\)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex EndsWithParenthesis();

    [GeneratedRegex(@"```csharp\r?\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex FencedBlock();

    #endregion

    #region Nested Types

    private sealed record Segment(string Code, bool IsCompliant);

    #endregion
}
