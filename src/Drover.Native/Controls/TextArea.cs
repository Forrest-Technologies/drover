namespace Drover.Native.Controls;

/// <summary>
/// Several lines of text with their label above. Text a machine reads, such as host names or a prompt, is set in
/// the machine typeface.
/// </summary>
public sealed class TextArea : StackPanel
{
    #region Private Fields

    private readonly TextBlock label = Styles.Text(null, "DvTextLabel");

    private readonly Facet frame = new();

    private readonly TextBox input = new()
    {
        Template = Styles.Template("DvTextBoxTemplate"),
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        UseSystemFocusVisuals = false,
    };

    private readonly TextBlock placeholder = Styles.Text(null);

    private readonly TextBlock hint = Styles.Text(null, "DvTextCaption");

    private bool focused;

    #endregion

    #region Constructors

    public TextArea()
    {
        Spacing = Tokens.Number("DvSpace1");
        var inside = new Thickness(Tokens.Number("DvSpace3"), Tokens.Number("DvSpace2"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace2"));
        input.Padding = inside;
        placeholder.Margin = inside;
        placeholder.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(placeholder, AccessibilityView.Raw);
        ScrollViewer.SetVerticalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        frame.Cut = Tokens.Number("DvCutS");
        frame.Children.Add(input);
        frame.Children.Add(placeholder);
        Children.Add(label);
        Children.Add(frame);
        Children.Add(hint);

        input.TextChanged += (_, _) => Take();
        input.GotFocus += (_, _) => SetFocused(true);
        input.LostFocus += (_, _) => SetFocused(false);
        Describe();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty = Dp.Register<TextArea, string>(nameof(Label), string.Empty, area => area.Describe());

    public static readonly DependencyProperty ValueProperty = Dp.Register<TextArea, string>(nameof(Value), string.Empty, area => area.ShowValue());

    public static readonly DependencyProperty RowsProperty = Dp.Register<TextArea, int>(nameof(Rows), 4, area => area.Describe());

    public static readonly DependencyProperty PlaceholderProperty = Dp.Register<TextArea, string?>(nameof(Placeholder), null, area => area.Describe());

    public static readonly DependencyProperty HintProperty = Dp.Register<TextArea, string?>(nameof(Hint), null, area => area.Describe());

    public static readonly DependencyProperty MachineProperty = Dp.Register<TextArea, bool>(nameof(Machine), false, area => area.Describe());

    public static readonly DependencyProperty DisabledProperty = Dp.Register<TextArea, bool>(nameof(Disabled), false, area => area.Describe());

    public static readonly DependencyProperty MaxLengthProperty = Dp.Register<TextArea, int>(nameof(MaxLength), 0, area => area.Describe());

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>How many lines are shown before the text scrolls.</summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>One line that helps fill the field in.</summary>
    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>The text is read by a machine, such as host names or a prompt, and is set in the machine typeface.</summary>
    public bool Machine
    {
        get => (bool)GetValue(MachineProperty);
        set => SetValue(MachineProperty, value);
    }

    public bool Disabled
    {
        get => (bool)GetValue(DisabledProperty);
        set => SetValue(DisabledProperty, value);
    }

    /// <summary>The most characters the field takes, or 0 for no limit. Typing past it does nothing.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    #endregion

    #region Public Methods

    /// <summary>Moves the focus into the field, for when it takes the place of the control a person just used.</summary>
    public void TakeFocus() => input.Focus(FocusState.Keyboard);

    #endregion

    #region Private Methods

    private void Take()
    {
        if (Value != input.Text)
        {
            Value = input.Text;
        }

        placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowValue()
    {
        if (input.Text != Value)
        {
            input.Text = Value;
        }

        placeholder.Visibility = Value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetFocused(bool value)
    {
        focused = value;
        Paint();
    }

    private void Describe()
    {
        var size = Tokens.Number(Machine ? "DvText13" : "DvText14");
        var line = size * Tokens.Number("DvLeadingText");

        label.Text = Label;
        placeholder.Text = Placeholder ?? string.Empty;
        placeholder.Visibility = Value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        hint.Text = Hint ?? string.Empty;
        hint.Visibility = Hint is null ? Visibility.Collapsed : Visibility.Visible;
        input.IsEnabled = !Disabled;
        input.MaxLength = MaxLength;
        input.FontFamily = placeholder.FontFamily = Machine ? Tokens.FontMono : Tokens.FontUi;
        input.FontSize = placeholder.FontSize = size;
        input.MinHeight = Math.Max(Tokens.Number("DvTargetTouch"), (Rows * line) + (2 * Tokens.Number("DvSpace2")));
        AutomationProperties.SetName(input, Label);
        AutomationProperties.SetHelpText(input, Hint ?? string.Empty);
        Paint();
    }

    private void Paint()
    {
        frame.Fill = Tokens.Brush(this, "DvBg0");
        frame.Edge = Tokens.Brush(this, focused ? "DvEmber" : "DvText3");
        frame.Ring = focused ? Tokens.Brush(this, "DvEmber") : null;
        input.Foreground = Tokens.Brush(this, Disabled ? "DvText3" : "DvText");
        placeholder.Foreground = Tokens.Brush(this, "DvText3");
    }

    #endregion
}
