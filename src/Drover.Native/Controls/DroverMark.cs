namespace Drover.Native.Controls;

/// <summary>
/// The Drover mark: the lead of the crew, the wedge head cocked, with the visor cut out of the head so it holds in
/// one color. It is decoration beside the word "Drover", so it has no name of its own.
/// </summary>
public sealed class DroverMark : SKCanvasElement
{
    #region Private Fields

    private const float Grid = 64;

    private const float Tilt = -7;

    private static readonly string[] parts = ["M12 14L52 14L56.2 27.5L9.6 21.7Z", "M7.2 30.4L53.8 36.2L44 55L20 55Z"];

    #endregion

    #region Constructors

    public DroverMark()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Width = Height = Tokens.Number("DvMark");
        this.WhenShownOrRethemed(Invalidate);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ForegroundProperty =
        Dp.Register<DroverMark, Brush?>(nameof(Foreground), null, mark => mark.Invalidate());

    /// <summary>The color of the mark. The text color when not set.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var color = (Foreground as SolidColorBrush)?.Color ?? Tokens.Color(this, "DvText");
        canvas.Scale((float)area.Width / Grid);
        canvas.RotateDegrees(Tilt, 32, 36);

        foreach (var part in parts)
        {
            ShapePainter.Fill(canvas, new SvgPath(part, null), color.ToSkia());
        }
    }

    #endregion
}
