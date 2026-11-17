namespace Drover.Native.Theme;

/// <summary>
/// Rounds a box fully, the way the full radius token does on the web: its ends become half circles. The renderer
/// takes a radius larger than half the box as an ellipse, so the radius follows the box's height.
/// </summary>
public static class Pill
{
    public static void Round(Border box)
    {
        box.SizeChanged += (_, _) => box.CornerRadius = new(Math.Min(Tokens.Number("DvRadiusFull"), box.ActualHeight / 2));
        box.CornerRadius = new(Math.Min(Tokens.Number("DvRadiusFull"), box.ActualHeight / 2));
    }
}
