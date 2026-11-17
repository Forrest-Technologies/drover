namespace Drover.Native.Controls;

/// <summary>
/// Something that is on or off, with its label beside it. On is shown by where the thumb is as well as by color.
/// It is a toggle to the platform: a tab stop, turned with Space and Enter, announced with its state.
/// </summary>
public sealed class Switch : ToggleButton
{
    #region Private Fields

    private readonly Border track = new();

    private readonly Border thumb = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock words = Styles.Text(null);

    private readonly TranslateTransform slide = new();

    private bool hasKeyboardFocus;

    #endregion

    #region Constructors

    public Switch()
    {
        Template = Styles.Template("DvPlainTemplate");
        UseSystemFocusVisuals = false;
        MinHeight = Targets.Minimum;
        HorizontalAlignment = HorizontalAlignment.Left;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Padding = new(0);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        track.Width = Tokens.Number("DvSwitchWidth");
        track.Height = Tokens.Number("DvSwitchHeight");
        track.CornerRadius = new(Tokens.Number("DvRadiusFull"));
        track.BorderThickness = new(Tokens.Number("DvBorder"));
        track.Padding = new(Tokens.Number("DvFocusWidth"), 0, Tokens.Number("DvFocusWidth"), 0);
        track.VerticalAlignment = VerticalAlignment.Center;
        thumb.Width = thumb.Height = Tokens.Number("DvSwitchThumb");
        thumb.CornerRadius = new(Tokens.Number("DvRadiusFull"));
        thumb.RenderTransform = slide;
        track.Child = thumb;
        words.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        row.Children.Add(new Border { Padding = new(Tokens.Number("DvFocusOffset") + Tokens.Number("DvFocusWidth")), Child = track });
        row.Children.Add(words);
        Content = row;

        Checked += (_, _) => Take();
        Unchecked += (_, _) => Take();
        GotFocus += (_, _) => SetKeyboardFocus(FocusState is FocusState.Keyboard);
        LostFocus += (_, _) => SetKeyboardFocus(false);
        IsEnabledChanged += (_, _) => Paint();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty = Dp.Register<Switch, string>(nameof(Label), string.Empty, toggle => toggle.Describe());

    public static readonly DependencyProperty AboutProperty = Dp.Register<Switch, string?>(nameof(About), null, toggle => toggle.Describe());

    public static readonly DependencyProperty ValueProperty =
        Dp.Register<Switch, bool>(nameof(Value), false, toggle => toggle.IsChecked = toggle.Value);

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>What the switch belongs to, read out after the label and not shown: for a row that already shows it.</summary>
    public string? About
    {
        get => (string?)GetValue(AboutProperty);
        set => SetValue(AboutProperty, value);
    }

    /// <summary>Whether it is on. It can be bound both ways.</summary>
    public bool Value
    {
        get => (bool)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    #endregion

    #region Private Methods

    private void Take()
    {
        Value = IsChecked is true;
        Paint();
    }

    private void SetKeyboardFocus(bool value)
    {
        hasKeyboardFocus = value;
        Paint();
    }

    private void Describe()
    {
        words.Text = Label;
        AutomationProperties.SetName(this, About is null ? Label : $"{Label}, {About}");
    }

    private void Paint()
    {
        var on = IsChecked is true;
        var travel = Tokens.Number("DvSwitchWidth") - Tokens.Number("DvSwitchThumb") - Tokens.Number("DvSpace2");

        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            IsEnabled ? Microsoft.UI.Input.InputSystemCursorShape.Hand : Microsoft.UI.Input.InputSystemCursorShape.UniversalNo);
        track.Background = Tokens.Brush(this, "DvBg1");
        track.BorderBrush = Tokens.Brush(this, !IsEnabled ? "DvLine" : on ? "DvEmber" : "DvText3");
        thumb.Background = Tokens.Brush(this, on ? "DvEmber" : "DvText3");
        slide.X = on ? travel : 0;
        words.Foreground = Tokens.Brush(this, "DvText");

        // The ring goes around the drawn switch, two wide with a gap of two, and only for the keyboard.
        var ringed = (Border)((StackPanel)Content).Children[0];
        ringed.BorderThickness = new(0);
        ringed.CornerRadius = new(Tokens.Number("DvRadiusFull"));
        ringed.Padding = new(Tokens.Number("DvFocusOffset"));
        ringed.BorderBrush = Tokens.Brush(this, "DvEmber");
        ringed.BorderThickness = new(hasKeyboardFocus ? Tokens.Number("DvFocusWidth") : 0);
        ringed.Margin = new(hasKeyboardFocus ? 0 : Tokens.Number("DvFocusWidth"));
    }

    #endregion
}
