namespace Drover.Native.Controls;

/// <summary>
/// The line of a running tool: a thin band of light that travels the top edge of its card, and of the composer
/// while a bot works. It moves only while <see cref="IsRunning"/> is set. With reduced motion it does not travel:
/// it lies still across the whole edge.
/// </summary>
public sealed class TravelLine : SKCanvasElement
{
    #region Private Fields

    // A fading theme's line lies still across the whole edge at this share of its strength, with no bloom.
    private const double FadedShare = 0.6;

    private static readonly TimeSpan frame = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer clock = new() { Interval = frame };

    private readonly System.Diagnostics.Stopwatch elapsed = new();

    #endregion

    #region Constructors

    public TravelLine()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Height = Tokens.Number("DvTravelLine") + (2 * Tokens.Number("DvBloomIon"));
        VerticalAlignment = VerticalAlignment.Top;
        clock.Tick += (_, _) => Tick();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => clock.Stop();
        ActualThemeChanged += (_, _) => Invalidate();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty IsRunningProperty =
        Dp.Register<TravelLine, bool>(nameof(IsRunning), false, line => line.Update());

    /// <summary>True while the tool runs. The line is not drawn at all otherwise.</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (!IsRunning)
        {
            return;
        }

        var thickness = (float)Tokens.Number("DvTravelLine");
        var ion = Tokens.Color(this, "DvIon").ToSkia();
        canvas.ClipRect(SKRect.Create(0, 0, (float)area.Width, (float)area.Height));

        if (Motion.Fades)
        {
            // One fade up to a line that lies still across the whole edge, and no light around it.
            var fade = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / Motion.FadeTime.TotalMilliseconds);
            using var still = new SKPaint { Color = ion.WithAlpha((byte)(255 * FadedShare * fade)) };
            canvas.DrawRect(0, 0, (float)area.Width, thickness, still);
            return;
        }

        var share = Motion.Loops ? (float)Tokens.Number("DvTravelWidth") : 1;
        var width = (float)area.Width * share;
        // From one length before the edge to four lengths along it, as the web client's keyframes say.
        var turn = (float)(elapsed.Elapsed.TotalMilliseconds / Tokens.Number("DvDurTravel") % 1);
        var left = Motion.Loops ? width * (-1 + (5 * turn)) : 0;
        using var light = new SKPaint
        {
            Color = ion,
            ImageFilter = SKImageFilter.CreateDropShadow(0, 0, (float)Tokens.Number("DvBloomIon") / 2, (float)Tokens.Number("DvBloomIon") / 2, ion),
        };
        canvas.DrawRect(left, 0, width, thickness, light);
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
