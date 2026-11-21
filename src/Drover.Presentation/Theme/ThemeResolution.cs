namespace Drover.Ui.Theme;

/// <summary>Works out the values a screen draws with from a theme file. Nothing derived is stored in the file.</summary>
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

    #endregion

    #region Public Methods

    public static ThemeTokenSet Resolve(this ThemeFile theme, ColorMode mode) =>
        new(theme, mode, Colors(theme, mode), Numbers(theme), Faces(theme));

    #endregion

    #region Private Methods

    private static Dictionary<string, Argb> Colors(ThemeFile theme, ColorMode mode)
    {
        var colors = theme.In(mode).Colors;
        var resolved = colors.ToDictionary(color => color.Key, color => Argb.Opaque(color.Value));
        resolved["scrim"] = Argb.At(colors["bg-0"], ScrimShare);
        resolved["yard-dot"] = Argb.At(colors["line"], theme.Surfaces.YardStrength);
        resolved["list"] = Argb.Opaque(theme.Surfaces.Levels == 3 ? colors["bg-1"] : colors["bg-0"]);

        return resolved;
    }

    private static Dictionary<string, double> Numbers(ThemeFile theme)
    {
        var (type, density, shape, surfaces, motion, characters) = (theme.Type, theme.Density, theme.Shape, theme.Surfaces, theme.Motion, theme.Characters);
        Dictionary<string, double> numbers = new()
        {
            ["leading-text"] = type.TextLeading,
            ["leading-heading"] = type.HeadingLeading,
            ["weight-body"] = type.BodyWeight,
            ["weight-label"] = type.LabelWeight,
            ["weight-heading"] = type.HeadingWeight,
            ["measure"] = Math.Round(type.Measure * FontFaces.Drawn(type.Reading ?? type.Interface).ZeroAdvance * type.Scale[BaseStep]),
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
