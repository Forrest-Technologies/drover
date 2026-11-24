namespace Drover.Presentation.Drawing;

/// <summary>An opaque color, as the tokens write one: "#05070A".</summary>
public readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    #region Private Fields

    private const double ChromaStep = 0.002;

    // A linear channel a screen can show lies between zero and one. A little past either end rounds back in.
    private const double GamutSlack = 0.001;

    #endregion

    #region Public Methods

    public static Rgb Parse(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length != 6)
        {
            throw new FormatException($"'{hex}' is not a color written as six hex digits.");
        }

        return new(Channel(digits, 0), Channel(digits, 2), Channel(digits, 4));
    }

    public override string ToString() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    /// <summary>
    /// The hue of this color in the Oklab space, in degrees from 0 up to 360. A gray has no hue: what it reads as
    /// here is noise, so ask only of a color that has chroma.
    /// </summary>
    public double Hue()
    {
        var (_, a, b) = ToOklab(this);
        var degrees = Math.Atan2(b, a) * 180 / Math.PI;

        return degrees < 0 ? degrees + 360 : degrees;
    }

    /// <summary>
    /// This color mixed with another in the Oklab space, the way the web client's style sheets mix a bot's head color
    /// with the visor or the light part of the head. <paramref name="share"/> is how much of this color is kept,
    /// from zero to one.
    /// </summary>
    public Rgb MixWith(Rgb other, double share)
    {
        var (l1, a1, b1) = ToOklab(this);
        var (l2, a2, b2) = ToOklab(other);
        var rest = 1 - share;

        return FromOklab((l1 * share) + (l2 * rest), (a1 * share) + (a2 * rest), (b1 * share) + (b2 * rest));
    }

    /// <summary>
    /// This color in the Oklab space as a lightness from zero to one, a chroma, and a hue in degrees from 0 up to
    /// 360.
    /// </summary>
    public (double Lightness, double Chroma, double Hue) ToOklch()
    {
        var (lightness, a, b) = ToOklab(this);

        return (lightness, Math.Sqrt((a * a) + (b * b)), Hue());
    }

    /// <summary>
    /// The color at this lightness, chroma and hue of the Oklab space. A chroma a screen cannot show is brought down
    /// until it can, so the hue asked for is the hue drawn.
    /// </summary>
    public static Rgb FromOklch(double lightness, double chroma, double hue)
    {
        var radians = hue * Math.PI / 180;

        for (var kept = chroma; kept > ChromaStep; kept -= ChromaStep)
        {
            var (a, b) = (kept * Math.Cos(radians), kept * Math.Sin(radians));
            if (Shows(lightness, a, b))
            {
                return FromOklab(lightness, a, b);
            }
        }

        return FromOklab(lightness, 0, 0);
    }

    #endregion

    #region Private Methods

    private static bool Shows(double lightness, double a, double b)
    {
        var (red, green, blue) = LinearFromOklab(lightness, a, b);

        double[] channels = [red, green, blue];

        return channels.All(channel => channel >= -GamutSlack && channel <= 1 + GamutSlack);
    }

    private static byte Channel(string digits, int start) =>
        byte.Parse(digits.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static double ToLinear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte FromLinear(double value)
    {
        var encoded = value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
        return (byte)Math.Round(Math.Clamp(encoded, 0, 1) * 255);
    }

    private static (double L, double A, double B) ToOklab(Rgb color)
    {
        var (r, g, b) = (ToLinear(color.Red), ToLinear(color.Green), ToLinear(color.Blue));
        var l = Math.Cbrt((0.4122214708 * r) + (0.5363325363 * g) + (0.0514459929 * b));
        var m = Math.Cbrt((0.2119034982 * r) + (0.6806995451 * g) + (0.1073969566 * b));
        var s = Math.Cbrt((0.0883024619 * r) + (0.2817188376 * g) + (0.6299787005 * b));

        return (
            (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s));
    }

    private static Rgb FromOklab(double lightness, double a, double b)
    {
        var (red, green, blue) = LinearFromOklab(lightness, a, b);

        return new(FromLinear(red), FromLinear(green), FromLinear(blue));
    }

    private static (double Red, double Green, double Blue) LinearFromOklab(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + (0.3963377774 * a) + (0.2158037573 * b), 3);
        var m = Math.Pow(lightness - (0.1055613458 * a) - (0.0638541728 * b), 3);
        var s = Math.Pow(lightness - (0.0894841775 * a) - (1.2914855480 * b), 3);

        return (
            (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s));
    }

    #endregion
}
