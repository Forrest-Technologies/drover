namespace Drover.Native.Controls;

/// <summary>
/// A piece of light that reports a live state: the dot or band of a bot that is thinking, the line work travels
/// along between bots. It is drawn in the state's accent with its bloom, the soft light around a live thing and
/// nothing else. Thinking breathes for as long as it lasts; work passing pulses once, out and back. With reduced
/// motion both are still, and fully lit.
/// </summary>
public sealed class LiveLight : SKCanvasElement
{
    #region Private Fields

    private const double Dim = 0.35;

    private static readonly TimeSpan frame = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer clock = new() { Interval = frame };

    private readonly System.Diagnostics.Stopwatch elapsed = new();

    #endregion

    #region Constructors

    public LiveLight()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        clock.Tick += (_, _) => Tick();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => clock.Stop();
        ActualThemeChanged += (_, _) => Invalidate();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty StateProperty =
        Dp.Register<LiveLight, LiveState>(nameof(State), LiveState.Thinking, light => light.Start());

    public static readonly DependencyProperty RoundProperty = Dp.Register<LiveLight, bool>(nameof(Round), false, light => light.Invalidate());

    /// <summary>Which live state this reports. It picks the accent and the motion.</summary>
    public LiveState State
    {
        get => (LiveState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Draws the light as a dot or a pill, where it is a band or a line otherwise.</summary>
    public bool Round
    {
        get => (bool)GetValue(RoundProperty);
        set => SetValue(RoundProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var passing = State is LiveState.Passing;
        var color = Tokens.Color(this, passing ? "DvViolet" : "DvIon").ToSkia();
        var bloom = (float)Tokens.Number(passing ? "DvBloomViolet" : "DvBloomIon");
        var radius = Round ? (float)Math.Min(area.Width, area.Height) / 2 : 0;

        using var light = new SKPaint
        {
            Color = color.WithAlpha((byte)(255 * Brightness())),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateDropShadow(0, 0, bloom / 2, bloom / 2, color.WithAlpha((byte)(255 * Brightness()))),
        };
        canvas.DrawRoundRect(SKRect.Create(0, 0, (float)area.Width, (float)area.Height), radius, radius, light);
    }

    private double Brightness()
    {
        if (Motion.IsReduced)
        {
            return 1;
        }

        var time = elapsed.Elapsed.TotalMilliseconds;
        if (State is LiveState.Thinking && Motion.Fades)
        {
            // One fade up to fully lit, then still: a theme that fades its states never loops them.
            return Dim + ((1 - Dim) * Math.Min(1, time / Motion.FadeTime.TotalMilliseconds));
        }

        if (State is LiveState.Thinking)
        {
            var turn = time / Tokens.Number("DvDurBreath") % 1;
            return Dim + ((1 - Dim) * (0.5 - (0.5 * Math.Cos(turn * 2 * Math.PI))));
        }

        // One pulse: up over one beat, back down over the next, then lit and still.
        var beat = Tokens.Number("DvDur3");
        return time >= 2 * beat ? 1 : Dim + ((1 - Dim) * (1 - Math.Abs((time / beat) - 1)));
    }

    private void Start()
    {
        clock.Stop();
        if (IsLoaded && !Motion.IsReduced)
        {
            elapsed.Restart();
            clock.Start();
        }

        Invalidate();
    }

    // The pulse ends after two beats, and its clock ends with it. Only thinking goes on, for as long as it lasts,
    // and in a theme that fades its states it ends with its one fade.
    private void Tick()
    {
        var time = elapsed.Elapsed.TotalMilliseconds;
        if ((State is LiveState.Passing && time >= 2 * Tokens.Number("DvDur3")) || (State is LiveState.Thinking && Motion.Fades && time >= Motion.FadeTime.TotalMilliseconds))
        {
            clock.Stop();
        }

        Invalidate();
    }

    #endregion
}
