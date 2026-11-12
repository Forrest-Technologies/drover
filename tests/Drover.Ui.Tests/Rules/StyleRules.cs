namespace Drover.Ui.Tests.Rules;

/// <summary>
/// The DESIGN.md rules that are about styles. Each takes the parsed blocks of one file and returns what breaks.
/// </summary>
public static class StyleRules
{
    #region Private Fields

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string LiveStates =
        ".dv-state-thinking, .dv-state-streaming, .dv-state-tool and .dv-state-delegation";

    private static readonly Regex quoted = new("\"[^\"\n]*\"|'[^'\n]*'", Options);

    private static readonly Regex resource = new(@"url\([^)]*\)", Options);

    private static readonly Regex tokenReference = new(@"var\(\s*--[\w-]+", Options);

    private static readonly Regex hexColor = new(@"#(?:[0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{3,4})\b", Options);

    private static readonly Regex colorFunction = new(
        @"(?<![\w-])(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\(",
        Options);

    private static readonly Regex measure = new(
        @"(?<![\w.#-])[-+]?(?<number>\d*\.?\d+)(?<unit>px|rem|em|pt|pc|cm|mm|in|ms|s)\b",
        Options);

    private static readonly Regex word = new(@"(?<![\w-])[a-z]+(?![\w(-])", Options);

    private static readonly Regex colorProperty = new(
        "(?:^|-)(?:color|background|border|outline|fill|stroke|shadow|decoration|rule|filter)",
        Options);

    private static readonly Regex heightProperty = new("^(?:min-|max-)?(?:height|block-size)$", Options);

    private static readonly Regex viewportHeight = new(@"(?<![\w.-])\d*\.?\d+vh\b", Options);

    private static readonly Regex infinite = new(@"(?<![\w-])infinite(?![\w-])", Options);

    private static readonly Regex stateClass = new(
        @"\.dv-state-(?:thinking|streaming|tool|delegation)(?![\w-])",
        Options);

    private static readonly Regex anyMotionQuery = new("prefers-reduced-motion", Options);

    private static readonly Regex reducedMotion = new(@"prefers-reduced-motion\s*:\s*reduce", Options);

    private static readonly Regex nothing = new(@"^\s*(?:none|0(?:px)?)\s*(?:!important\s*)?$", Options);

    private static readonly string[] focusReplacements =
        ["box-shadow", "border", "border-color", "border-width", "border-style"];

    private static readonly HashSet<string> namedColors =
    [
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black", "blanchedalmond",
        "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate", "coral",
        "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod", "darkgray",
        "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange", "darkorchid",
        "darkred", "darksalmon", "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey",
        "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick",
        "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green",
        "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
        "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan",
        "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon",
        "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue", "lightyellow",
        "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid",
        "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen", "mediumturquoise",
        "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin", "navajowhite", "navy", "oldlace",
        "olive", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
        "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple",
        "rebeccapurple", "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen",
        "seashell", "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen",
        "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white", "whitesmoke",
        "yellow", "yellowgreen",
    ];

    #endregion

    #region Public Methods

    /// <summary>
    /// A color, a length, or a duration written out instead of taken from tokens.css.
    /// Fine: zero with or without a unit, unitless numbers (opacity, line height, weights, counts),
    /// percentages, fr, ch, viewport units, angles, <c>transparent</c>, <c>currentColor</c>, and anything
    /// in a selector or an at-rule condition (keyframe steps, media queries, where a token cannot be used).
    /// A fallback inside <c>var(--token, 4px)</c> is a raw value.
    /// </summary>
    public static IEnumerable<Violation> NoRawValues(string file, IReadOnlyList<StyleRule> rules) =>
        rules.SelectMany(rule => rule.Declarations).SelectMany(declaration => RawValues(file, declaration));

    /// <summary>
    /// A raw color in an SVG presentation attribute, where the same rule applies as in a style sheet.
    /// </summary>
    public static bool IsRawColor(string value)
    {
        var reduced = Reduced(value);
        return hexColor.IsMatch(reduced) || colorFunction.IsMatch(reduced) || NamedColors(reduced).Any();
    }

    /// <summary>
    /// A looping animation anywhere but in one of the four live-state classes.
    /// </summary>
    public static IEnumerable<Violation> MotionOnlyForLiveState(string file, IReadOnlyList<StyleRule> rules) =>
        from rule in rules
        where !IsLiveState(rule)
        from declaration in rule.Declarations
        where declaration.Property is "animation" or "animation-iteration-count" && infinite.IsMatch(declaration.Value)
        select new Violation(
            RuleNames.MotionOnlyForLiveState,
            file,
            declaration.Line,
            $"'{Describe(rule)}' loops but is not a live state; only {LiveStates} may animate without end");

    /// <summary>
    /// A live-state class that animates with no rule for the same class under reduced motion in the same file.
    /// An animation already inside a <c>prefers-reduced-motion</c> query needs nothing more.
    /// </summary>
    public static IEnumerable<Violation> ReducedMotionHonored(string file, IReadOnlyList<StyleRule> rules)
    {
        HashSet<string> calmed = [.. rules.Where(IsUnderReducedMotion).SelectMany(StateClasses)];
        return
            from rule in rules
            where !rule.Context.Any(anyMotionQuery.IsMatch)
            from declaration in rule.Declarations
            where declaration.Property is "animation" or "animation-name" && !nothing.IsMatch(declaration.Value)
            from state in StateClasses(rule).Distinct()
            where !calmed.Contains(state)
            select new Violation(
                RuleNames.ReducedMotionHonored,
                file,
                declaration.Line,
                $"{state} animates here and has no rule under '@media (prefers-reduced-motion: reduce)' in this file");
    }

    /// <summary>
    /// A height in <c>vh</c>, which mobile browser bars cut off. <c>dvh</c>, <c>svh</c> and <c>lvh</c> are fine.
    /// </summary>
    public static IEnumerable<Violation> NoViewportHeight(string file, IReadOnlyList<StyleRule> rules) =>
        from rule in rules
        from declaration in rule.Declarations
        where heightProperty.IsMatch(declaration.Property)
        from Match match in viewportHeight.Matches(Reduced(declaration.Value))
        select new Violation(
            RuleNames.NoViewportHeight,
            file,
            declaration.LineOf(match.Index),
            $"'{declaration.Property}: {match.Value}' is cut off by mobile browser bars; use dvh, svh or lvh");

    /// <summary>
    /// An outline removed with nothing put in its place. A box-shadow or a border in the same block replaces it.
    /// </summary>
    public static IEnumerable<Violation> FocusAlwaysVisible(string file, IReadOnlyList<StyleRule> rules) =>
        from rule in rules
        where !rule.Declarations.Any(ReplacesFocus)
        from declaration in rule.Declarations
        where declaration.Property is "outline" or "outline-style" or "outline-width"
        where nothing.IsMatch(declaration.Value)
        select new Violation(
            RuleNames.FocusAlwaysVisible,
            file,
            declaration.Line,
            $"'{Describe(rule)}' removes the outline and sets no box-shadow or border in its place");

    #endregion

    #region Raw Values

    private static IEnumerable<Violation> RawValues(string file, StyleDeclaration declaration)
    {
        var value = Reduced(declaration.Value);
        foreach (Match match in hexColor.Matches(value))
        {
            yield return Raw(file, declaration, match.Index, $"'{match.Value}' is a raw color");
        }

        value = SourceText.BlankMatches(value, hexColor);
        foreach (var (index, text) in ColorFunctions(value))
        {
            yield return Raw(file, declaration, index, $"'{text}' is a raw color");
            value = SourceText.BlankRange(value, index, index + text.Length);
        }

        if (colorProperty.IsMatch(declaration.Property))
        {
            foreach (var match in NamedColors(value))
            {
                yield return Raw(file, declaration, match.Index, $"'{match.Value}' is a raw color");
            }
        }

        foreach (var match in measure.Matches(value).Where(IsNotZero))
        {
            var kind = match.Groups["unit"].Value.ToLowerInvariant() is "ms" or "s" ? "duration" : "size";
            yield return Raw(file, declaration, match.Index, $"'{match.Value}' is a raw {kind}");
        }
    }

    private static Violation Raw(string file, StyleDeclaration declaration, int index, string what) =>
        new(
            RuleNames.NoRawValues,
            file,
            declaration.LineOf(index),
            $"{what} in '{declaration.Property}'; use a token from tokens.css");

    private static string Reduced(string value)
    {
        var withoutText = SourceText.BlankMatches(SourceText.BlankMatches(value, quoted), resource);
        return SourceText.BlankMatches(withoutText, tokenReference);
    }

    private static List<(int Index, string Text)> ColorFunctions(string value) =>
    [
        .. colorFunction.Matches(value).Select(match =>
        {
            var end = SourceText.SkipBalanced(value, match.Index + match.Length - 1, '(', ')');
            return (match.Index, value[match.Index..end]);
        }),
    ];

    private static IEnumerable<Match> NamedColors(string value) =>
        word.Matches(value).Where(match => namedColors.Contains(match.Value.ToLowerInvariant()));

    private static bool IsNotZero(Match match) =>
        double.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) != 0;

    #endregion

    #region Selectors

    private static bool IsLiveState(StyleRule rule) =>
        rule.Context.Any(stateClass.IsMatch) || Selectors(rule.Selector).All(stateClass.IsMatch);

    private static bool IsUnderReducedMotion(StyleRule rule) => rule.Context.Any(reducedMotion.IsMatch);

    private static IEnumerable<string> StateClasses(StyleRule rule) =>
        rule.Context.Append(rule.Selector).SelectMany(text => stateClass.Matches(text)).Select(match => match.Value);

    private static bool ReplacesFocus(StyleDeclaration declaration) =>
        focusReplacements.Contains(declaration.Property) && !nothing.IsMatch(declaration.Value);

    private static string Describe(StyleRule rule) => Regex.Replace(rule.Selector, @"\s+", " ");

    private static List<string> Selectors(string selector)
    {
        List<string> selectors = [];
        var start = 0;
        var index = 0;
        while (index < selector.Length)
        {
            if (selector[index] is '(' or '[')
            {
                var close = selector[index] is '(' ? ')' : ']';
                index = SourceText.SkipBalanced(selector, index, selector[index], close);
                continue;
            }

            if (selector[index] is ',')
            {
                selectors.Add(selector[start..index]);
                start = index + 1;
            }

            index++;
        }

        selectors.Add(selector[start..]);
        return selectors;
    }

    #endregion
}
