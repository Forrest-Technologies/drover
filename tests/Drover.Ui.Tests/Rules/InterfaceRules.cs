namespace Drover.Ui.Tests.Rules;

/// <summary>
/// Checks one interface file against the rules DESIGN.md marks as tested.
/// Pure: the same path and text always give the same result.
/// </summary>
public static class InterfaceRules
{
    #region Private Fields

    private const string TokensFile = "tokens.css";

    private static readonly Regex allowComment = new(
        @"/\*\s*design:(?<body>.*?)\*/|@\*\s*design:(?<body>.*?)\*@",
        RegexOptions.Singleline);

    private static readonly Regex allowBody = new(
        @"^\s*allow\s+(?<rule>[\w-]+)\s+because\s+(?<reason>\S.*?)\s*$",
        RegexOptions.Singleline);

    #endregion

    #region Public Methods

    /// <summary>
    /// Scans one <c>.css</c> or <c>.razor</c> file. A violation covered by an allow comment is left out
    /// and the allowance is returned instead, so it can be shown.
    /// </summary>
    public static ScanResult Scan(string relativePath, string text)
    {
        var (allowances, malformed) = ReadAllowComments(relativePath, text);
        var found = Check(relativePath, text);
        List<Allowance> used = [.. allowances.Where(allowance => found.Any(allowance.Covers))];
        List<Violation> standing =
        [
            .. found.Where(violation => !allowances.Any(allowance => allowance.Covers(violation))),
            .. malformed,
            .. allowances.Except(used).Select(AllowsNothing),
        ];
        return new([.. standing.OrderBy(violation => violation.Line)], used);
    }

    #endregion

    #region Checks

    private static List<Violation> Check(string file, string text)
    {
        var document = file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ? Markup.Parse(text) : null;
        var styles = document?.Styles ?? StyleSheet.Parse(text);
        return
        [
            .. IsTokensFile(file) ? [] : StyleRules.NoRawValues(file, styles),
            .. StyleRules.MotionOnlyForLiveState(file, styles),
            .. StyleRules.ReducedMotionHonored(file, styles),
            .. StyleRules.NoViewportHeight(file, styles),
            .. StyleRules.FocusAlwaysVisible(file, styles),
            .. NoEmoji(file, text),
            .. document is null ? [] : CheckMarkup(file, document),
        ];
    }

    private static IEnumerable<Violation> CheckMarkup(string file, MarkupDocument document) =>
    [
        .. MarkupRules.NoRawColors(file, document),
        .. MarkupRules.ControlsAreRealControls(file, document),
        .. MarkupRules.IconOnlyControlsAreNamed(file, document),
        .. MarkupRules.FieldsAreLabeled(file, document),
        .. MarkupRules.MediaHasDimensions(file, document),
        .. MarkupRules.NoHypeWords(file, document),
    ];

    private static bool IsTokensFile(string file) =>
        string.Equals(file.Replace('\\', '/').Split('/')[^1], TokensFile, StringComparison.Ordinal);

    /// <summary>
    /// An emoji anywhere in the file, comments and code included. Arrows, mathematical signs, and the
    /// copyright, registered, and trademark signs are text and are fine, unless forced to emoji presentation.
    /// </summary>
    private static IEnumerable<Violation> NoEmoji(string file, string text) =>
        text.Split('\n')
            .Select((line, index) => (Line: index + 1, Emoji: line.EnumerateRunes().Where(IsEmoji).ToList()))
            .Where(line => line.Emoji.Count > 0)
            .Select(line => new Violation(
                RuleNames.NoEmoji,
                file,
                line.Line,
                $"emoji {string.Join(", ", line.Emoji.Select(CodePoint))}; use words or an icon from the set"));

    private static string CodePoint(Rune rune) => $"U+{rune.Value:X4}";

    private static bool IsEmoji(Rune rune) =>
        rune.Value is (>= 0x1F000 and <= 0x1FAFF)
            or (>= 0x2600 and <= 0x27BF)
            or (>= 0x231A and <= 0x231B)
            or (>= 0x23E9 and <= 0x23FA)
            or (>= 0x2B1B and <= 0x2B1C)
            or 0x2B50
            or 0x2B55
            or 0x20E3
            or 0xFE0F;

    #endregion

    #region Allow Comments

    private static (List<Allowance> Allowances, List<Violation> Malformed) ReadAllowComments(string file, string text)
    {
        List<Allowance> allowances = [];
        List<Violation> malformed = [];
        foreach (Match comment in allowComment.Matches(text))
        {
            var line = SourceText.LineAt(text, comment.Index);
            var body = allowBody.Match(comment.Groups["body"].Value);
            if (!body.Success)
            {
                malformed.Add(new(
                    RuleNames.AllowComment,
                    file,
                    line,
                    "an allow comment reads 'design: allow <rule> because <reason>'; the reason is required"));
            }
            else if (!RuleNames.Allowable.Contains(body.Groups["rule"].Value))
            {
                var known = string.Join(", ", RuleNames.Allowable);
                malformed.Add(new(
                    RuleNames.AllowComment,
                    file,
                    line,
                    $"'{body.Groups["rule"].Value}' is not a rule; the rules are {known}"));
            }
            else
            {
                var lineStart = text.LastIndexOf('\n', Math.Max(comment.Index - 1, 0)) + 1;
                var followsCode = !string.IsNullOrWhiteSpace(text[lineStart..comment.Index]);
                var lastLine = SourceText.LineAt(text, comment.Index + comment.Length) + (followsCode ? 0 : 1);
                allowances.Add(new(body.Groups["rule"].Value, file, line, lastLine, body.Groups["reason"].Value));
            }
        }

        return (allowances, malformed);
    }

    private static Violation AllowsNothing(Allowance allowance) =>
        new(
            RuleNames.AllowComment,
            allowance.File,
            allowance.Line,
            $"this comment allows {allowance.Rule} but nothing it covers breaks that rule; remove it");

    #endregion
}
