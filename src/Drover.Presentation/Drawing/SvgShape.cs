namespace Drover.Presentation.Drawing;

/// <summary>
/// One shape of a drawing kept as data: an icon's strokes, a part of a bot's character. The class names the role
/// the shape plays, so the control that draws it picks its color.
/// </summary>
public abstract record SvgShape(string? Class);

/// <summary>A path, in the path language icons and characters are written in.</summary>
public sealed record SvgPath(string Data, string? Class) : SvgShape(Class);

public sealed record SvgRect(double X, double Y, double Width, double Height, double Radius, string? Class)
    : SvgShape(Class);

public sealed record SvgCircle(double CenterX, double CenterY, double Radius, string? Class) : SvgShape(Class);
