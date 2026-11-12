namespace Drover.Ui.Tests.Rules;

/// <summary>
/// WCAG 2 contrast between two opaque <c>#RRGGBB</c> colors.
/// </summary>
public static class Contrast
{
    #region Public Methods

    public static double Ratio(string first, string second)
    {
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    #endregion

    #region Private Methods

    private static double Luminance(string hex)
    {
        var digits = hex.Trim().TrimStart('#');
        if (digits.Length != 6)
        {
            throw new FormatException($"'{hex}' is not a #RRGGBB color.");
        }

        return (0.2126 * Channel(digits[..2])) + (0.7152 * Channel(digits[2..4])) + (0.0722 * Channel(digits[4..]));
    }

    private static double Channel(string pair)
    {
        var value = int.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    #endregion
}
