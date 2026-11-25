namespace Drover.Native.Controls;

/// <summary>
/// The line of live work: a thin band of light in the theme's own color that travels around the outline of its
/// frame, in the frame's corner language, while a tool runs or a bot works. It lies over the frame, set out with
/// <see cref="Over"/> so its light has room, and moves only while <see cref="IsRunning"/> is set. With reduced motion
/// it does not travel: it lies still around the whole outline.
/// </summary>
public sealed class TravelLine : SKCanvasElement
{
    #region Private Fields

    // A fading theme's line lies still around the whole outline at this share of its strength, with no bloom.
    private const double FadedShare = 0.6;

    // The line goes this many of its own lengths in one travel duration, as it did along the top edge.
    private const double LengthsPerTurn = 5;

    private static readonly TimeSpan frame = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer clock = new() { Interval = frame };

    private readonly System.Diagnostics.Stopwatch elapsed = new();

    #endregion

    #region Constructors

    public TravelLine()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        clock.Tick += (_, _) => Tick();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => clock.Stop();
        ActualThemeChanged += (_, _) => Invalidate();
        SizeChanged += (_, _) => Invalidate();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty IsRunningProperty =
        Dp.Register<TravelLine, bool>(nameof(IsRunning), false, line => line.Update());

    public static readonly DependencyProperty RoleProperty =
        Dp.Register<TravelLine, FacetRole>(nameof(Role), FacetRole.Frame, line => line.Invalidate());

    public static readonly DependencyProperty CutProperty =
        Dp.Register<TravelLine, double>(nameof(Cut), double.NaN, line => line.Invalidate());

    /// <summary>True while the work runs. The line is not drawn at all otherwise.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>The role of the frame the line goes around, which decides the size of its corners.</summary>
    public FacetRole Role
    {
        get => (FacetRole)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>The cut of the frame the line goes around, when the frame sets its own. Not a number otherwise.</summary>
    public double Cut
    {
        get => (double)GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// The margin that lays the line on the edge of a frame with this padding, with room outside it for the light.
    /// </summary>
    public static Thickness Over(Thickness padding)
    {
        var bloom = Bloom;
        return new(-padding.Left - bloom, -padding.Top - bloom, -padding.Right - bloom, -padding.Bottom - bloom);
    }

    #endregion

    #region Private Methods

    private static double Bloom => Tokens.Number("DvBloomIon");

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (!IsRunning)
        {
            return;
        }

        using var outline = Outline(area);
        if (outline is null)
        {
            return;
        }

        var color = Tokens.Color(this, "DvEmber").ToSkia();

        if (Motion.Fades)
        {
            // One fade up to a line that lies still around the whole outline, and no light around it.
            var fade = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / Motion.FadeTime.TotalMilliseconds);
            using var still = Paint(color.WithAlpha((byte)(255 * FadedShare * fade)), glow: false);
            canvas.DrawPath(outline, still);
            return;
        }

        using var light = Paint(color, glow: true);
        if (!Motion.Loops)
        {
            canvas.DrawPath(outline, light);
            return;
        }

        using var measure = new SKPathMeasure(outline, true);
        using var piece = Piece(measure, area);
        canvas.DrawPath(piece, light);
    }

    // The frame's outline, inside the room left for the light, or nothing while the frame has no size.
    private SKPath? Outline(Size area)
    {
        var bloom = Bloom;
        var (width, height) = (area.Width - (2 * bloom), area.Height - (2 * bloom));
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var cut = double.IsNaN(Cut) ? FrameOutline.CutOf(Role) : Cut;
        var corners = FrameOutline.Of(
            ThemeTokens.Current.Shape.Corners, width, height, Tokens.Number("DvTravelLine") / 2, cut, FrameOutline.RadiusOf(Role));
        var path = new SKPath();
        path.MoveTo((float)(corners[0].X + bloom), (float)(corners[0].Y + bloom));
        foreach (var corner in corners.Skip(1))
        {
            path.LineTo((float)(corner.X + bloom), (float)(corner.Y + bloom));
        }

        path.Close();
        return path;
    }

    // The stretch of the outline the line covers now. It keeps the length it had on the top edge, a share of the
    // frame's width, and its speed, so it goes around a larger frame in a longer time.
    private SKPath Piece(SKPathMeasure measure, Size area)
    {
        var around = measure.Length;
        var length = (float)Math.Min(around, (area.Width - (2 * Bloom)) * Tokens.Number("DvTravelWidth"));
        var turns = elapsed.Elapsed.TotalMilliseconds / Tokens.Number("DvDurTravel");
        var start = (float)(turns * LengthsPerTurn * length % around);
        var piece = new SKPath();
        measure.GetSegment(start, start + length, piece, true);
        if (start + length > around)
        {
            measure.GetSegment(0, start + length - around, piece, true);
        }

        return piece;
    }

    private static SKPaint Paint(SKColor color, bool glow)
    {
        var bloom = (float)Bloom / 2;
        return new()
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Tokens.Number("DvTravelLine"),
            StrokeJoin = SKStrokeJoin.Miter,
            IsAntialias = true,
            ImageFilter = glow ? SKImageFilter.CreateDropShadow(0, 0, bloom, bloom, color) : null,
        };
    }

    // A fading theme's line has one fade to show, and its clock ends with it. A looping line goes on while it runs.
    private void Tick()
    {
        Invalidate();
        if (Motion.Fades && elapsed.Elapsed >= Motion.FadeTime)
        {
            clock.Stop();
        }
    }

    private void Update()
    {
        if (IsRunning && !Motion.IsReduced)
        {
            elapsed.Restart();
            clock.Start();
        }
        else
        {
            clock.Stop();
        }

        Invalidate();
    }

    #endregion
}
