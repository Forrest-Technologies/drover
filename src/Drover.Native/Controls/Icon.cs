namespace Drover.Native.Controls;

/// <summary>
/// One icon of Drover's own set, drawn from the shapes in <see cref="IconShapes"/>: straight segments, mitered
/// joins, one stroke weight. It is decoration beside words or inside a named control, so it has no name of its own.
/// </summary>
public sealed class Icon : SKCanvasElement
{
    #region Private Fields

    private const float Grid = 24;

    #endregion

    #region Constructors

    public Icon()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Resize();
        this.WhenShownOrRethemed(Invalidate);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty NameOfIconProperty =
        Dp.Register<Icon, IconName>(nameof(NameOfIcon), IconName.Info, icon => icon.Invalidate());

    public static readonly DependencyProperty SizeProperty = Dp.Register<Icon, int>(nameof(Size), 16, icon => icon.Resize());

    public static readonly DependencyProperty ForegroundProperty =
        Dp.Register<Icon, Brush?>(nameof(Foreground), null, icon => icon.Invalidate());

    public static readonly DependencyProperty ColorTokenProperty =
        Dp.Register<Icon, string?>(nameof(ColorToken), null, icon => icon.Invalidate());

    /// <summary>Which icon. Named so because every element already has a <c>Name</c>.</summary>
    public IconName NameOfIcon
    {
        get => (IconName)GetValue(NameOfIconProperty);
        set => SetValue(NameOfIconProperty, value);
    }

    /// <summary>The edge length in pixels: 16 or 20.</summary>
    public int Size
    {
        get => (int)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The stroke brush when no color token is specified. Defaults to the text color.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The theme token for an icon that carries its own color inside another control.</summary>
    public string? ColorToken
    {
        get => (string?)GetValue(ColorTokenProperty);
        set => SetValue(ColorTokenProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var color = ColorToken is { } token
            ? Tokens.Color(this, token)
            : (Foreground as SolidColorBrush)?.Color ?? Tokens.Color(this, "DvText");
        canvas.Scale((float)area.Width / Grid);

        foreach (var shape in SvgParts.Read(IconShapes.For(NameOfIcon)))
        {
            ShapePainter.Stroke(canvas, shape, color.ToSkia(), (float)Tokens.Number("DvIconStroke"), round: false);
        }
    }

    private void Resize()
    {
        Width = Height = Tokens.Number(Size >= 20 ? "DvIcon20" : "DvIcon16");
        Invalidate();
    }

    #endregion
}
