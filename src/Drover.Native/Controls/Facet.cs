namespace Drover.Native.Controls;

/// <summary>
/// The frame: a surface with the corners of the theme in use. This is the one place corners are drawn. A faceted
/// theme cuts two corners, top left and bottom right, echoing the bot heads; a square theme rounds all four by a
/// small radius and a rounded theme by a large one, up to half the frame's height; a ticked theme leaves the rectangle and marks the same two corners with ticks, which cross out
/// past the corner as registration marks when the theme says so. Controls take the control size and larger frames
/// the frame size. It is a panel: whatever is put in it sits on the surface. A clipped frame cannot show a ring
/// outside itself, so its focus ring is drawn inside the edge.
/// </summary>
public sealed class Facet : Grid
{
    #region Private Fields

    private const int EveryCell = 64;

    private readonly Polygon surface = new() { IsHitTestVisible = true };

    private readonly Polygon ring = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    // The two ticks of a ticked frame, one at each marked corner.
    private readonly Polyline[] ticks = [new() { IsHitTestVisible = false }, new() { IsHitTestVisible = false }];

    private readonly Canvas backdrop = new();

    #endregion

    #region Constructors

    public Facet()
    {
        // The shapes sit on a canvas, which asks for no room of its own: the frame takes its size from what is
        // put in it, and the shapes follow that size, never the other way round.
        Role = FacetRole.Frame;
        backdrop.Children.Add(surface);
        backdrop.Children.Add(ring);
        ticks.ToList().ForEach(backdrop.Children.Add);
        SetRowSpan(backdrop, EveryCell);
        SetColumnSpan(backdrop, EveryCell);
        Children.Add(backdrop);

        SizeChanged += (_, _) => Draw();
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Properties

    /// <summary>The corner language this frame was last drawn in, which the screen checks read.</summary>
    internal CornerLanguage DrawnCorners { get; private set; }

    /// <summary>
    /// The size of a cut, a tick or a radius this frame was last drawn with, which the screen checks read.
    /// </summary>
    internal double DrawnSize { get; private set; }

    public static readonly DependencyProperty RoleProperty =
        Dp.Register<Facet, FacetRole>(nameof(Role), FacetRole.Frame, facet => facet.Draw());

    public static readonly DependencyProperty CutProperty =
        Dp.Register<Facet, double>(nameof(Cut), double.NaN, facet => facet.Draw());

    public static readonly DependencyProperty FillProperty = Dp.Register<Facet, Brush?>(nameof(Fill), null, facet => facet.Draw());

    public static readonly DependencyProperty EdgeProperty = Dp.Register<Facet, Brush?>(nameof(Edge), null, facet => facet.Draw());

    public static readonly DependencyProperty RingProperty = Dp.Register<Facet, Brush?>(nameof(Ring), null, facet => facet.Draw());

    /// <summary>Whether this is a control or a larger frame, which decides the size of its corner.</summary>
    public FacetRole Role
    {
        get => (FacetRole)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>
    /// The length of each cut corner of a faceted frame, when it is not the theme's for its role. Not a number
    /// until it is set: the frame then takes <c>DvCutS</c> as a control and <c>DvCut</c> as a larger frame.
    /// </summary>
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
        var corners = ThemeTokens.Current.Shape.Corners;

        // The frame's padding is for what is put in it. The surface reaches the frame's own edge.
        backdrop.Margin = new(-Padding.Left, -Padding.Top, -Padding.Right, -Padding.Bottom);

        surface.Fill = Fill ?? Tokens.Brush(this, "DvBg1");
        surface.Stroke = Edge ?? Tokens.Brush(this, "DvLine");
        surface.StrokeThickness = border;
        surface.Points = Outline(corners, border / 2);

        ring.Visibility = Ring is null ? Visibility.Collapsed : Visibility.Visible;
        ring.Stroke = Ring;
        ring.StrokeThickness = focus;
        ring.Points = Outline(corners, focus / 2);

        DrawTicks(corners, focus);
        var cut = double.IsNaN(Cut) ? FrameOutline.CutOf(Role) : Cut;
        (DrawnCorners, DrawnSize) = (corners, corners is CornerLanguage.Square or CornerLanguage.Rounded ? FrameOutline.RadiusOf(Role) : cut);
    }

    // The corner outline, moved in by half the line so the whole line is inside the frame.
    private PointCollection Outline(CornerLanguage corners, double inset) =>
        [.. FrameOutline.Of(corners, ActualWidth, ActualHeight, inset, double.IsNaN(Cut) ? FrameOutline.CutOf(Role) : Cut, FrameOutline.RadiusOf(Role))];

    // A tick is an L of the theme's tick length at the top left and the bottom right. With registration marks the
    // L stands out past the corner instead of on it.
    private void DrawTicks(CornerLanguage corners, double thickness)
    {
        var shown = corners is CornerLanguage.Ticked;
        var length = FrameOutline.CutOf(Role);
        var outside = ThemeTokens.Current.Shape.Registration
            ? Tokens.Number("DvSpace1") + (thickness / 2)
            : thickness / 2;
        var (width, height) = (ActualWidth, ActualHeight);

        foreach (var tick in ticks)
        {
            tick.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            tick.Stroke = Tokens.Brush(this, "DvText3");
            tick.StrokeThickness = thickness;
        }

        ticks[0].Points = [new(-outside, length - outside), new(-outside, -outside), new(length - outside, -outside)];
        ticks[1].Points =
        [
            new(width + outside, height - length + outside),
            new(width + outside, height + outside),
            new(width - length + outside, height + outside),
        ];
    }

    #endregion
}
