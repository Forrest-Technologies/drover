namespace Drover.Contracts.Themes;

/// <summary>
/// A color that may be see-through, as the token file writes it: "#05070A", or "#B805070A" with the alpha first.
/// </summary>
public readonly record struct Argb(byte Alpha, Rgb Color)
{
    #region Properties

    public static Argb Opaque(Rgb color) => new(byte.MaxValue, color);

    #endregion

    #region Public Methods

    public static Argb Parse(string hex)
    {
        var digits = hex.TrimStart('#');

        return digits.Length switch
        {
            6 => Opaque(Rgb.Parse(digits)),
            8 => new(
                byte.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                Rgb.Parse(digits[2..])),
            _ => throw new FormatException($"'{hex}' is not a color written as six or eight hex digits."),
        };
    }

    /// <summary>
    /// This color at a share of its strength, from zero to one: the scrim is the first surface at 72 percent.
    /// </summary>
    public static Argb At(Rgb color, double share) =>
        new((byte)Math.Round(Math.Clamp(share, 0, 1) * byte.MaxValue, MidpointRounding.AwayFromZero), color);

    public override string ToString() =>
        Alpha == byte.MaxValue ? Color.ToString() : $"#{Alpha:X2}{Color.ToString()[1..]}";

    #endregion
}
