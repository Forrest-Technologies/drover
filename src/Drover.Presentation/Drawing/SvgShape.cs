namespace Drover.Presentation.Drawing;

/// <summary>
/// One shape of a drawing kept as data: an icon's strokes, a part of a bot's character. The class names the role
/// the shape plays, so the control that draws it picks its color.
/// </summary>
public abstract record SvgShape(string? Class)
{
    /// <summary>The shape's outline in the path language, so one kind of drawing code draws every shape.</summary>
    public abstract string PathData { get; }

    private protected static string Text(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>A path, in the path language icons and characters are written in.</summary>
public sealed record SvgPath(string Data, string? Class) : SvgShape(Class)
{
    public override string PathData => Data;
}

public sealed record SvgRect(double X, double Y, double Width, double Height, double Radius, string? Class)
    : SvgShape(Class)
{
    public override string PathData
    {
        get
        {
            var radius = Math.Min(Radius, Math.Min(Width, Height) / 2);
            var (left, top, right, bottom) = (X, Y, X + Width, Y + Height);
            if (radius <= 0)
            {
                return $"M{Text(left)} {Text(top)}H{Text(right)}V{Text(bottom)}H{Text(left)}Z";
            }

            var corner = $"A{Text(radius)} {Text(radius)} 0 0 1";
            string[] sides =
            [
                $"M{Text(left + radius)} {Text(top)}H{Text(right - radius)}{corner} {Text(right)} {Text(top + radius)}",
                $"V{Text(bottom - radius)}{corner} {Text(right - radius)} {Text(bottom)}",
                $"H{Text(left + radius)}{corner} {Text(left)} {Text(bottom - radius)}",
                $"V{Text(top + radius)}{corner} {Text(left + radius)} {Text(top)}Z",
            ];
            return string.Concat(sides);
        }
    }
}

public sealed record SvgCircle(double CenterX, double CenterY, double Radius, string? Class) : SvgShape(Class)
{
    public override string PathData
    {
        get
        {
            var half = $"A{Text(Radius)} {Text(Radius)} 0 1 0";
            var (left, right, middle) = (Text(CenterX - Radius), Text(CenterX + Radius), Text(CenterY));
            return $"M{left} {middle}{half} {right} {middle}{half} {left} {middle}Z";
        }
    }
}
