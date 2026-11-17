namespace Drover.Presentation.Drawing;

/// <summary>
/// Reads the shapes out of a drawing kept as markup, such as <see cref="IconShapes"/> and the parts of a
/// <see cref="BotLook"/>. Those are written once, as data, and every client draws from the same data: the web
/// places the markup in a picture element, and a native client draws the shapes read here.
/// </summary>
public static class SvgParts
{
    #region Public Methods

    public static IReadOnlyList<SvgShape> Read(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return [];
        }

        var root = XElement.Parse($"<g>{markup}</g>");
        return [.. root.Elements().Select(Shape)];
    }

    #endregion

    #region Private Methods

    private static SvgShape Shape(XElement element)
    {
        var role = (string?)element.Attribute("class");

        return element.Name.LocalName switch
        {
            "path" => new SvgPath(Text(element, "d"), role),
            "rect" => new SvgRect(
                Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height"),
                Number(element, "rx"), role),
            "circle" => new SvgCircle(Number(element, "cx"), Number(element, "cy"), Number(element, "r"), role),
            var name => throw new FormatException($"The drawing holds a '{name}', and only paths, rects, and circles are drawn."),
        };
    }

    private static string Text(XElement element, string name) =>
        (string?)element.Attribute(name) ?? throw new FormatException($"A {element.Name.LocalName} in the drawing has no '{name}'.");

    private static double Number(XElement element, string name) =>
        (string?)element.Attribute(name) is { } text ? double.Parse(text, CultureInfo.InvariantCulture) : 0;

    #endregion
}
