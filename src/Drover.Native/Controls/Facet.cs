namespace Drover.Native.Controls;

/// <summary>
/// The faceted frame: a surface with two cut corners, top left and bottom right, echoing the bot heads. Controls
/// cut 6, larger frames cut 10. It is a panel: whatever is put in it sits on the surface. A clipped frame cannot
/// show a ring outside itself, so its focus ring is drawn inside the edge.
/// </summary>
public sealed class Facet : Grid
{
    #region Private Fields

    private const int EveryCell = 64;

    private readonly Polygon surface = new() { IsHitTestVisible = true };

    private readonly Polygon ring = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    private readonly Canvas backdrop = new();

    #endregion

    #region Constructors

    public Facet()
    {
        // The shapes sit on a canvas, which asks for no room of its own: the frame takes its size from what is
        // put in it, and the shapes follow that size, never the other way round.
        Cut = Tokens.Number("DvCut");
        backdrop.Children.Add(surface);
        backdrop.Children.Add(ring);
        SetRowSpan(backdrop, EveryCell);
        SetColumnSpan(backdrop, EveryCell);
        Children.Add(backdrop);

        SizeChanged += (_, _) => Draw();
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CutProperty = Dp.Register<Facet, double>(nameof(Cut), 10, facet => facet.Draw());

    public static readonly DependencyProperty FillProperty = Dp.Register<Facet, Brush?>(nameof(Fill), null, facet => facet.Draw());

    public static readonly DependencyProperty EdgeProperty = Dp.Register<Facet, Brush?>(nameof(Edge), null, facet => facet.Draw());

    public static readonly DependencyProperty RingProperty = Dp.Register<Facet, Brush?>(nameof(Ring), null, facet => facet.Draw());

    /// <summary>The length of each cut corner: <c>DvCutS</c> on a control, <c>DvCut</c> on a larger frame.</summary>
    public double Cut
    {
        get => (double)GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    /// <summary>The surface. The panel color when not set.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The one pixel line around the surface. The decorative line color when not set.</summary>
    public Brush? Edge
    {
        get => (Brush?)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    /// <summary>The focus ring, drawn inside the edge while this is set.</summary>
    public Brush? Ring
    {
        get => (Brush?)GetValue(RingProperty);
        set => SetValue(RingProperty, value);
    }

    #endregion

    #region Private Methods

    private void Draw()
    {
        var border = Tokens.Number("DvBorder");
        var focus = Tokens.Number("DvFocusWidth");

        // The frame's padding is for what is put in it. The surface reaches the frame's own edge.
        backdrop.Margin = new(-Padding.Left, -Padding.Top, -Padding.Right, -Padding.Bottom);

        surface.Fill = Fill ?? Tokens.Brush(this, "DvBg1");
        surface.Stroke = Edge ?? Tokens.Brush(this, "DvLine");
        surface.StrokeThickness = border;
        surface.Points = Outline(border / 2);

        ring.Visibility = Ring is null ? Visibility.Collapsed : Visibility.Visible;
        ring.Stroke = Ring;
        ring.StrokeThickness = focus;
        ring.Points = Outline(focus / 2);
    }

    // The six corners, moved in by half the line so the whole line is inside the frame.
    private PointCollection Outline(double inset)
    {
        var (width, height, cut) = (ActualWidth, ActualHeight, Cut);

        return
        [
            new(cut, inset),
            new(width - inset, inset),
            new(width - inset, height - cut),
            new(width - cut, height - inset),
            new(inset, height - inset),
            new(inset, cut),
        ];
    }

    #endregion
}
