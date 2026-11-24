namespace Drover.Ui.Theme;

/// <summary>
/// Works out the values a screen draws with from a theme file. Nothing derived is stored in the file.
/// </summary>
public static class ThemeResolution
{
    #region Private Fields

    // The 4 px base steps: space-1 to space-7.
    private static readonly int[] spaceSteps = [4, 8, 12, 16, 24, 32, 48];

    // The three durations the scale multiplies, in ms.
    private static readonly int[] durations = [120, 220, 420];

    // The scrim over a dialog is the first surface at 72 percent.
    private const double ScrimShare = 0.72;

    private const int BaseStep = 2;

    // In a theme whose conversation is bubbles, a person's bubble is the color of their intent at this share, over
    // the panel surface: a tint dark enough in a dark mode, and pale enough in a light one, for text to stand on.
    private const double BubbleShare = 0.3;

    #endregion

    #region Public Methods

    public static ThemeTokenSet Resolve(this ThemeFile theme, ColorMode mode) =>
        new(theme, mode, Colors(theme, mode), Numbers(theme), Faces(theme));

    /// <summary>
    /// The surface of a person's message: the card surface where a bot's words run as a document, and a tint of the
    /// color of the person's intent where both sides speak in bubbles.
    /// </summary>
    public static Rgb Bubble(ThemeFile theme, ColorMode mode)
    {
        var colors = theme.In(mode).Colors;

        return theme.Surfaces.Conversation is ConversationStyle.Bubbles
            ? colors["ember"].MixWith(colors["bg-1"], BubbleShare)
            : colors["bg-2"];
    }

    #endregion

    #region Private Methods

    private static Dictionary<string, Argb> Colors(ThemeFile theme, ColorMode mode)
    {
        var colors = theme.In(mode).Colors;
        var resolved = colors.ToDictionary(color => color.Key, color => Argb.Opaque(color.Value));
        resolved["scrim"] = Argb.At(colors["bg-0"], ScrimShare);
        resolved["yard-dot"] = Argb.At(colors["line"], theme.Surfaces.YardStrength);
        resolved["list"] = Argb.Opaque(theme.Surfaces.Levels == 3 ? colors["bg-1"] : colors["bg-0"]);
        resolved["bubble"] = Argb.Opaque(Bubble(theme, mode));

        return resolved;
    }

    private static Dictionary<string, double> Numbers(ThemeFile theme)
    {
        var (type, density, shape) = (theme.Type, theme.Density, theme.Shape);
        var (surfaces, motion, characters) = (theme.Surfaces, theme.Motion, theme.Characters);
        var readingFace = FontFaces.Drawn(type.Reading ?? type.Interface);
        Dictionary<string, double> numbers = new()
        {
            ["leading-text"] = type.TextLeading,
            ["leading-heading"] = type.HeadingLeading,
            ["weight-body"] = type.BodyWeight,
            ["weight-label"] = type.LabelWeight,
            ["weight-heading"] = type.HeadingWeight,
            ["measure"] = Math.Round(type.Measure * readingFace.ZeroAdvance * type.Scale[BaseStep]),
            ["tracking"] = Math.Round(type.Tracking * 1000),
            ["tracking-heading"] = Math.Round(type.HeadingTracking * 1000),
            ["target-pointer"] = density.Control.ForPointer,
            ["target-touch"] = density.Control.ForTouch,
            ["row-pointer"] = density.Row.ForPointer,
            ["row-touch"] = density.Row.ForTouch,
            ["composer-lines"] = density.ComposerLines,
            ["radius-control"] = shape.ControlRadius,
            ["radius-card"] = shape.CardRadius,
            ["radius-sheet"] = shape.SheetRadius,
            ["cut-s"] = shape.Cut.Control,
            ["cut"] = shape.Cut.Frame,
            ["border"] = shape.Line,
            ["icon-stroke"] = shape.IconStroke,
            ["grid-step"] = surfaces.YardStep,
            ["dur-breath"] = motion.Breath,
            ["dur-travel"] = motion.Travel,
            ["bloom-ion"] = motion.Bloom,
            ["bloom-violet"] = motion.Bloom,
            ["travel-line"] = motion.TravelLine,
            ["character-facet"] = characters.Facet,
            ["character-line"] = characters.Line,
        };

        foreach (var (size, step) in type.Scale.Select((size, step) => (size, step)))
        {
            numbers[$"size-{step}"] = size;
        }

        AddLines(numbers, theme);

        foreach (var (spaceStep, index) in spaceSteps.Select((step, index) => (step, index)))
        {
            numbers[$"space-{index + 1}"] = Math.Max(2, Math.Round(spaceStep * density.Spacing));
        }

        foreach (var (duration, index) in durations.Select((duration, index) => (duration, index)))
        {
            numbers[$"dur-{index + 1}"] = Math.Round(duration * motion.Scale);
        }

        return numbers;
    }

    // A style cannot multiply, so a line height is worked out here: the size of a step times the leading. A browser
    // centers a line's letters in its line height, and the native text box keeps them at the top, so each face also
    // gets the lead it moves its letters down by: half of what the line has over the height the face asks of a line.
    private static void AddLines(Dictionary<string, double> numbers, ThemeFile theme)
    {
        var (type, faces) = (theme.Type, Faces(theme));
        var (text, heading) = (type.TextLeading, type.HeadingLeading);

        foreach (var step in (int[])[0, 1, 2])
        {
            numbers[$"line-{step}"] = type.Scale[step] * text;
        }

        numbers["line-h1"] = type.Scale[5] * heading;
        numbers["line-h2"] = type.Scale[4] * heading;
        numbers["line-h3"] = type.Scale[3] * heading;
        numbers["lead-body"] = Lead(numbers["line-2"], type.Scale[2], faces.Interface);
        numbers["lead-mono"] = Lead(numbers["line-0"], type.Scale[0], faces.Machine);
    }

    private static double Lead(double line, int size, FontFaces.Face face) =>
        Math.Round((line - (size * face.LineFactor)) / 2, MidpointRounding.AwayFromZero);

    private static ThemeFaces Faces(ThemeFile theme)
    {
        var type = theme.Type;
        var (face, machine) = (FontFaces.Drawn(type.Interface), FontFaces.Drawn(type.Machine));

        return new(
            face,
            machine,
            type.Reading is null ? face : FontFaces.Drawn(type.Reading),
            type.Headings is HeadingFace.Machine ? machine : face,
            type.MachineNumerals ? machine : face);
    }

    #endregion
}
