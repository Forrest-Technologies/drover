namespace Drover.Native.Controls;

/// <summary>
/// A short word about a state, in the machine typeface on a pill. The words carry the meaning; the kind only adds
/// color.
/// </summary>
public sealed class Badge : Grid
{
    #region Private Fields

    private readonly Border pill = new();

    private readonly TextBlock words = Styles.Text(null, "DvTextMono");

    #endregion

    #region Constructors

    public Badge()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        pill.Child = words;
        pill.Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), 0);
        pill.BorderThickness = new(Tokens.Number("DvBorder"));
        Pill.Round(pill);
        Children.Add(pill);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TextProperty =
        Dp.Register<Badge, string>(nameof(Text), string.Empty, badge => badge.words.Text = badge.Text);

    public static readonly DependencyProperty KindProperty =
        Dp.Register<Badge, BadgeKind>(nameof(Kind), BadgeKind.Neutral, badge => badge.Paint());

    public static readonly DependencyProperty WrapsProperty = Dp.Register<Badge, bool>(
        nameof(Wraps), false, badge => badge.words.TextWrapping = badge.Wraps ? TextWrapping.Wrap : TextWrapping.NoWrap);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public BadgeKind Kind
    {
        get => (BadgeKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Lets the words wrap, for a badge in a narrow lane. A badge keeps to one line otherwise.</summary>
    public bool Wraps
    {
        get => (bool)GetValue(WrapsProperty);
        set => SetValue(WrapsProperty, value);
    }

    #endregion

    #region Private Methods

    private void Paint()
    {
        var (edge, ink) = Kind switch
        {
            BadgeKind.Live => ("DvIon", "DvIon"),
            BadgeKind.Ok => ("DvOk", "DvOk"),
            BadgeKind.Warn => ("DvWarn", "DvWarn"),
            BadgeKind.Danger => ("DvDanger", "DvDanger"),
            _ => ("DvText3", "DvText2"),
        };

        pill.BorderBrush = Tokens.Brush(this, edge);
        words.Foreground = Tokens.Brush(this, ink);
        words.TextWrapping = Wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    #endregion
}
