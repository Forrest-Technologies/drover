namespace Drover.Ui.Theme;

/// <summary>The WCAG 2 contrast ratio between two opaque colors, from 1 (the same) up to 21 (black on white).</summary>
public static class WcagContrast
{
    #region Public Methods

    public static double Ratio(Rgb first, Rgb second)
    {
        var (firstLuminance, secondLuminance) = (Luminance(first), Luminance(second));
        var (lighter, darker) = (Math.Max(firstLuminance, secondLuminance), Math.Min(firstLuminance, secondLuminance));

        return (lighter + 0.05) / (darker + 0.05);
    }

    #endregion

    #region Private Methods

    private static double Luminance(Rgb color) =>
        (0.2126 * Channel(color.Red)) + (0.7152 * Channel(color.Green)) + (0.0722 * Channel(color.Blue));

    private static double Channel(byte value)
    {
        var scaled = value / 255.0;

        return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }

    #endregion
}
