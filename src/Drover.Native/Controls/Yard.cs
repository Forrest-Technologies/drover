namespace Drover.Native.Controls;

/// <summary>
/// The yard: the faint texture behind the working area, dots, lines or a grid, or nothing, as the theme says. It
/// fills whatever it is given and sits behind the content; lists and panels stay plain.
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

    #region Properties

    /// <summary>The texture and step this yard was last drawn with, which the screen checks read.</summary>
    internal (YardTexture Texture, double Step) Drawn { get; private set; }

    #endregion

    #region Private Methods

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var texture = ThemeTokens.Current.Surfaces.Texture;
        Drawn = (texture, Tokens.Number("DvGridStep"));
        if (texture is YardTexture.None)
        {
            return;
        }

        var step = (float)Tokens.Number("DvGridStep");
        var weight = (float)Tokens.Number("DvBorder");
        using var paint = new SKPaint { Color = Tokens.Color(this, "DvYardDot").ToSkia(), IsAntialias = true };

        if (texture is YardTexture.Dots)
        {
            DrawDots(canvas, area, step, weight, paint);
            return;
        }

        // A line sits on the last pixel row of each cell, and a grid adds the same down the last column.
        for (var y = step - weight; y < area.Height; y += step)
        {
            canvas.DrawRect(0, y, (float)area.Width, weight, paint);
        }

        for (var x = step - weight; texture is YardTexture.Grid && x < area.Width; x += step)
        {
            canvas.DrawRect(x, 0, weight, (float)area.Height, paint);
        }
    }

    private static void DrawDots(SKCanvas canvas, Size area, float step, float dot, SKPaint paint)
    {
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
