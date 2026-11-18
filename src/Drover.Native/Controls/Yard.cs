namespace Drover.Native.Controls;

/// <summary>
/// The yard: the faint dot grid behind the working area. It fills whatever it is given and sits behind the
/// content; lists and panels stay plain.
/// </summary>
public sealed class Yard : SKCanvasElement
{
    #region Constructors

    public Yard()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        this.WhenShownOrRethemed(Invalidate);
    }

    #endregion

    #region Private Methods

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var step = (float)Tokens.Number("DvGridStep");
        var dot = (float)Tokens.Number("DvBorder");
        using var paint = new SKPaint { Color = Tokens.Color(this, "DvYardDot").ToSkia(), IsAntialias = true };

        for (var y = step / 2; y < area.Height; y += step)
        {
            for (var x = step / 2; x < area.Width; x += step)
            {
                canvas.DrawCircle(x, y, dot, paint);
            }
        }
    }

    #endregion
}
