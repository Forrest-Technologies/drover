namespace Drover.Native.Theme;

/// <summary>
/// The motion of the interface. Four things move, because four things are states worth seeing: a bot thinking, text
/// streaming, a tool running, work passing between bots. Each has a method here, and with reduced motion set each
/// leaves its element still in a static equivalent. Nothing else in the app starts a repeating animation.
/// </summary>
public static class Motion
{
    #region Private Fields

    private const double Dim = 0.35;

    private static readonly Dictionary<UIElement, Storyboard> running = [];

    #endregion

    #region Properties

    /// <summary>True when the person asked the device for less motion. Set once by the platform at startup.</summary>
    public static bool IsReduced { get; set; }

    /// <summary>True when a live state loops for as long as it lasts: motion is wanted, and the theme loops its states.</summary>
    public static bool Loops => !IsReduced && ThemeTokens.Current.Motion.Live is LiveStyle.Loop;

    /// <summary>
    /// True when a live state shows as one fade to a still state, never a loop: motion is wanted, and the theme fades
    /// its states. The state is said in words either way, as every live state is.
    /// </summary>
    public static bool Fades => !IsReduced && ThemeTokens.Current.Motion.Live is LiveStyle.Fade;

    /// <summary>How long the one fade of a fading theme takes.</summary>
    public static TimeSpan FadeTime => TimeSpan.FromMilliseconds(Tokens.Number("DvDur3"));

    #endregion

    #region Public Methods

    /// <summary>A token duration as a time: zero when motion is reduced, as the web client's tokens are.</summary>
    public static TimeSpan Duration(string key) =>
        IsReduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Tokens.Number(key));

    /// <summary>
    /// A bot is thinking: a slow breath of light, or in a theme that fades its states one fade up to fully lit.
    /// Still and fully lit with reduced motion.
    /// </summary>
    public static void Breathe(UIElement element)
    {
        if (IsReduced)
        {
            element.Opacity = 1;
            return;
        }

        if (Fades)
        {
            Start(element, new DoubleAnimation { From = Dim, To = 1, Duration = FadeTime });
            return;
        }

        var breath = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            Duration = TimeSpan.FromMilliseconds(Tokens.Number("DvDurBreath")),
        };
        var half = TimeSpan.FromMilliseconds(Tokens.Number("DvDurBreath") / 2);
        breath.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = Dim });
        breath.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = half, Value = 1, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        breath.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = half + half, Value = Dim, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Start(element, breath);
    }

    /// <summary>Text is streaming: what just arrived fades in. It simply appears with reduced motion.</summary>
    public static void Arrive(UIElement element)
    {
        if (IsReduced)
        {
            element.Opacity = 1;
            return;
        }

        Start(element, new DoubleAnimation { From = 0, To = 1, Duration = Duration("DvDur1") });
    }

    /// <summary>Work passes between bots: one pulse, out and back. Still with reduced motion.</summary>
    public static void Pulse(UIElement element)
    {
        if (IsReduced)
        {
            element.Opacity = 1;
            return;
        }

        var pulse = new DoubleAnimationUsingKeyFrames();
        var beat = Duration("DvDur3");
        pulse.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = Dim });
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = beat, Value = 1 });
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = beat + beat, Value = Dim });
        pulse.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = beat + beat, Value = 1 });
        Start(element, pulse);
    }

    /// <summary>An element appears: it fades in over a token duration. This is a transition, not a state.</summary>
    public static void FadeIn(UIElement element, string durationKey = "DvDur2")
    {
        if (IsReduced)
        {
            element.Opacity = 1;
            return;
        }

        Start(element, new DoubleAnimation { From = 0, To = 1, Duration = Duration(durationKey) });
    }

    /// <summary>The work ended, so the motion ends with it and the element is left as it is at rest.</summary>
    public static void Rest(UIElement element)
    {
        if (running.Remove(element, out var storyboard))
        {
            storyboard.Stop();
        }

        element.Opacity = 1;
    }

    #endregion

    #region Private Methods

    private static void Start(UIElement element, Microsoft.UI.Xaml.Media.Animation.Timeline animation)
    {
        Rest(element);
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => running.Remove(element);
        running[element] = storyboard;
        storyboard.Begin();
    }

    #endregion
}
