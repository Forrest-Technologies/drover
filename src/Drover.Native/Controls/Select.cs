namespace Drover.Native.Controls;

/// <summary>
/// One choice from a list, with its label above. The list is the platform's own, so it opens, scrolls, and takes
/// the keyboard as lists do everywhere else on the device.
/// </summary>
// The control keeps the name it has in the web client, and nothing in another language consumes this app.
#pragma warning disable CA1716
public sealed class Select : StackPanel
#pragma warning restore CA1716
{
    #region Private Fields

    private static readonly string[] surfaces =
    [
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed", "ComboBoxBackgroundDisabled",
        "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundFocused", "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver",
        "ComboBoxBorderBrushPressed", "ComboBoxBorderBrushDisabled", "ComboBoxBackgroundBorderBrushFocused",
        "ComboBoxBackgroundBorderBrushUnfocused",
    ];

    private readonly TextBlock label = Styles.Text(null, "DvTextLabel");

    private readonly Facet frame = new();

    private readonly ComboBox list = new() { HorizontalAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = false };

    private bool focused;

    #endregion

    #region Constructors

    public Select()
    {
        Spacing = Tokens.Number("DvSpace1");
        frame.Role = FacetRole.Control;
        frame.MinHeight = Targets.Minimum;
        frame.Children.Add(list);
        Children.Add(label);
        Children.Add(frame);

        list.MinHeight = Targets.Minimum;
        list.FontFamily = Tokens.FontUi;
        list.FontSize = Tokens.Number("DvText14");
        list.Padding = new(Tokens.Number("DvSpace3"), 0, 0, 0);
        list.BorderThickness = new(0);
        list.CornerRadius = new(0);
        list.SelectionChanged += (_, _) => Take();
        list.GotFocus += (_, _) => SetFocused(true);
        list.LostFocus += (_, _) => SetFocused(false);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty = Dp.Register<Select, string>(nameof(Label), string.Empty, select => select.Describe());

    public static readonly DependencyProperty OptionsProperty =
        Dp.Register<Select, IReadOnlyList<SelectOption>>(nameof(Options), [], select => select.ShowOptions());

    public static readonly DependencyProperty ValueProperty = Dp.Register<Select, string>(nameof(Value), string.Empty, select => select.ShowValue());

    public static readonly DependencyProperty DisabledProperty = Dp.Register<Select, bool>(nameof(Disabled), false, select => select.Describe());

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The choices, in the order shown.</summary>
    public IReadOnlyList<SelectOption> Options
    {
        get => (IReadOnlyList<SelectOption>)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>The value of the chosen option. It can be bound both ways.</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool Disabled
    {
        get => (bool)GetValue(DisabledProperty);
        set => SetValue(DisabledProperty, value);
    }

    #endregion

    #region Private Methods

    private void ShowOptions()
    {
        list.ItemsSource = Options;
        ShowValue();
    }

    private void ShowValue() => list.SelectedItem = Options.FirstOrDefault(option => option.Value == Value);

    private void Take()
    {
        if (list.SelectedItem is SelectOption chosen && chosen.Value != Value)
        {
            Value = chosen.Value;
        }
    }

    private void SetFocused(bool value)
    {
        focused = value;
        Paint();
    }

    private void Describe()
    {
        label.Text = Label;
        list.IsEnabled = !Disabled;
        AutomationProperties.SetName(list, Label);
        Paint();
    }

    // The frame draws the surface and the edge, so the list's own are cleared in every state.
    private void Paint()
    {
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in surfaces)
        {
            list.Resources[key] = clear;
        }

        list.Resources["ComboBoxForeground"] = Tokens.Brush(this, "DvText");
        list.Resources["ComboBoxForegroundDisabled"] = Tokens.Brush(this, "DvText3");
        list.Resources["ComboBoxDropDownGlyphForeground"] = Tokens.Brush(this, "DvText2");
        list.Resources["ComboBoxDropDownBackground"] = Tokens.Brush(this, "DvBg2");
        list.Resources["ComboBoxDropDownBorderBrush"] = Tokens.Brush(this, "DvLine");
        list.Foreground = Tokens.Brush(this, Disabled ? "DvText3" : "DvText");
        frame.Fill = Tokens.Brush(this, "DvBg0");
        frame.Edge = Tokens.Brush(this, focused ? "DvEmber" : "DvText3");
        frame.Ring = focused ? Tokens.Brush(this, "DvEmber") : null;
    }

    #endregion
}
