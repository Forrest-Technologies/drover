namespace Drover.Ui.Theme;

/// <summary>
/// The accessibility gates a theme must pass, run over the resolved values of a theme in every mode: dark, light,
/// and the black mode worked out from the dark one. A theme file
/// that fails one cannot ship: the tests run these over every file in <c>Theme/Themes</c>, so a new theme cannot
/// merge without passing. The bounds and the contrast pairs are the ones in
/// <c>docs/architecture/09-design-direction.md</c>, "The token groups and their bounds". The shared checker of
/// issue 778 runs the same gates on a person's own theme before anything is applied.
/// </summary>
public static class ThemeGates
{
    #region Private Fields

    private const double TextMinimum = 4.5;

    private const double InterfaceMinimum = 3;

    // The accent roles are a person's intent, a bot working, and work passing between bots. They stay three colors
    // a person can tell apart by hue as well as by place: at least this far apart, in degrees of the Oklab hue
    // circle. The seven themes of the design are 60 degrees apart at the closest.
    private const double AccentHueMinimum = 45;

    private const int PointerMinimum = 32;

    private const int TouchMinimum = 44;

    private static readonly string[] surfaces = ["bg-0", "bg-1", "bg-2", "bg-3"];

    private static readonly string[] textAndStatus =
        ["text", "text-2", "text-3", "ember", "ion", "violet", "ok", "warn", "danger"];

    private static readonly string[] bubbleText = ["text", "text-2"];

    private static readonly string[] visors = ["visor", "visor-lit", "visor-failed"];

    private static readonly string[] accents = ["ember", "ion", "violet"];

    private static readonly string[] forbiddenWords = ["gradient", "shadow"];

    private static readonly string[] hues = [.. Enumerable.Range(0, 12).Select(step => $"hue-{step}")];

    private static readonly string[] heads = [.. Enumerable.Range(0, 12).Select(step => $"head-{step}")];

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs every gate over the text of a theme file: the bounds first, by reading it in the closed format, and when
    /// it reads, the rest over its resolved values. <paramref name="expectedId"/> is the file's name without
    /// <c>.json</c>.
    /// </summary>
    public static IReadOnlyList<GateFailure> CheckFile(string json, string expectedId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return [.. NamesNoGradientOrShadow(document.RootElement, expectedId), .. CheckReadable(json, expectedId)];
        }
        catch (JsonException exception)
        {
            return [new(expectedId, null, ThemeGate.Bounds, $"the text is not JSON: {exception.Message}")];
        }
    }

    private static IReadOnlyList<GateFailure> CheckReadable(string json, string expectedId)
    {
        try
        {
            return Check(ThemeFileReader.Read(json, expectedId));
        }
        catch (ThemeFormatException exception)
        {
            return
            [
                .. exception.Problems.Select(problem => new GateFailure(expectedId, null, ThemeGate.Bounds, problem)),
            ];
        }
    }

    /// <summary>Runs every gate that works on resolved values over a theme, in each of its modes.</summary>
    public static IReadOnlyList<GateFailure> Check(ThemeFile theme)
    {
        List<GateFailure> failures = [];

        foreach (var mode in Enum.GetValues<ColorMode>())
        {
            var colors = theme.In(mode).Colors;
            failures.AddRange(TextContrast(theme.Id, mode, colors));
            failures.AddRange(Bubble(theme, mode));
            failures.AddRange(InterfaceContrast(theme.Id, mode, colors));
            failures.AddRange(BotHues(theme.Id, mode, colors));
            failures.AddRange(AccentsAreDistinct(theme.Id, mode, colors));
        }

        failures.AddRange(TouchTargets(theme));

        return failures;
    }

    #endregion

    #region Contrast

    private static IEnumerable<GateFailure> TextContrast(
        string id,
        ColorMode mode,
        IReadOnlyDictionary<string, Rgb> colors)
    {
        var gate = ThemeGate.TextContrast;

        return Pairs(id, mode, gate, colors, textAndStatus, surfaces, TextMinimum)
            .Concat(Pairs(id, mode, gate, colors, ["on-accent"], ["ember"], TextMinimum));
    }

    // A person's words stand on their bubble in the text color, and what is said under them in the secondary one.
    private static IEnumerable<GateFailure> Bubble(ThemeFile theme, ColorMode mode)
    {
        var bubble = ThemeResolution.Bubble(theme, mode);
        var colors = theme.In(mode).Colors;

        foreach (var foreground in bubbleText)
        {
            var ratio = WcagContrast.Ratio(colors[foreground], bubble);
            if (ratio < TextMinimum)
            {
                var what = $"{foreground} on the bubble {bubble} is {ratio:0.00}:1, needs {TextMinimum}:1";
                yield return new(theme.Id, mode, ThemeGate.TextContrast, what);
            }
        }
    }

    // A control's edge is drawn in text-3, and the focus ring in ember, on whatever surface the control stands on.
    private static IEnumerable<GateFailure> InterfaceContrast(
        string id,
        ColorMode mode,
        IReadOnlyDictionary<string, Rgb> colors) =>
        Pairs(id, mode, ThemeGate.InterfaceContrast, colors, ["text-3", "ember"], surfaces, InterfaceMinimum);

    private static IEnumerable<GateFailure> BotHues(string id, ColorMode mode, IReadOnlyDictionary<string, Rgb> colors)
    {
        var gate = ThemeGate.BotHues;

        return Pairs(id, mode, gate, colors, hues, surfaces, TextMinimum)
            .Concat(Pairs(id, mode, gate, colors, visors, heads, InterfaceMinimum));
    }

    private static IEnumerable<GateFailure> Pairs(
        string id,
        ColorMode mode,
        ThemeGate gate,
        IReadOnlyDictionary<string, Rgb> colors,
        IEnumerable<string> foregrounds,
        IEnumerable<string> backgrounds,
        double minimum)
    {
        List<string> against = [.. backgrounds];

        foreach (var foreground in foregrounds)
        {
            foreach (var background in against)
            {
                var ratio = WcagContrast.Ratio(colors[foreground], colors[background]);
                if (ratio < minimum)
                {
                    var what = $"{foreground} on {background} is {ratio:0.00}:1, needs {minimum}:1";
                    yield return new(id, mode, gate, what);
                }
            }
        }
    }

    #endregion

    #region Invariants and targets

    private static IEnumerable<GateFailure> AccentsAreDistinct(
        string id,
        ColorMode mode,
        IReadOnlyDictionary<string, Rgb> colors)
    {
        for (var first = 0; first < accents.Length; first++)
        {
            for (var second = first + 1; second < accents.Length; second++)
            {
                var apart = HueDistance(colors[accents[first]], colors[accents[second]]);
                if (apart < AccentHueMinimum)
                {
                    var pair = $"{accents[first]} and {accents[second]}";
                    var what = $"{pair} are {apart:0.0} degrees apart in hue, needs {AccentHueMinimum}";
                    yield return new(id, mode, ThemeGate.Invariants, what);
                }
            }
        }
    }

    private static double HueDistance(Rgb first, Rgb second)
    {
        var apart = Math.Abs(first.Hue() - second.Hue());

        return Math.Min(apart, 360 - apart);
    }

    private static IEnumerable<GateFailure> TouchTargets(ThemeFile theme)
    {
        var (control, row) = (theme.Density.Control, theme.Density.Row);
        var heights = new (string Name, int Value, int Needs)[]
        {
            ("control height for a pointer", control.ForPointer, PointerMinimum),
            ("control height for a finger", control.ForTouch, TouchMinimum),
            ("list row height for a pointer", row.ForPointer, PointerMinimum),
            ("list row height for a finger", row.ForTouch, TouchMinimum),
        };

        return heights.Where(height => height.Value < height.Needs).Select(height => new GateFailure(
            theme.Id, null, ThemeGate.TouchTargets, $"{height.Name} is {height.Value} px, needs {height.Needs} px"));
    }

    // The format is closed, so a theme cannot name a gradient or a shadow: a key that says so is a theme that tries.
    private static IEnumerable<GateFailure> NamesNoGradientOrShadow(JsonElement root, string id) =>
        PropertyNames(root)
            .Where(name => forbiddenWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(name => new GateFailure(id, null, ThemeGate.Invariants, ForbiddenName(name)));

    private static string ForbiddenName(string name) =>
        $"the file names '{name}': depth is surface and line only, so a theme has no gradient and no shadow";

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(property => PropertyNames(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    #endregion
}
