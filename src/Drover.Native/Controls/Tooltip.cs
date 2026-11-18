namespace Drover.Native.Controls;

/// <summary>
/// A short line about the control it wraps, shown while the pointer is over the control or the keyboard focus is
/// on it. The same line describes the control to assistive technology. Nothing depends on it: it only adds.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Described))]
public sealed class Tooltip : Grid
{
    #region Private Fields

    private readonly ToolTip tip = new() { Placement = PlacementMode.Bottom };

    #endregion

    #region Constructors

    public Tooltip()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        this.WhenShownOrRethemed(Describe);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TextProperty = Dp.Register<Tooltip, string>(nameof(Text), string.Empty, tooltip => tooltip.Describe());

    public static readonly DependencyProperty DescribedProperty =
        Dp.Register<Tooltip, UIElement?>(nameof(Described), null, tooltip => tooltip.Describe());

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The control the tooltip describes.</summary>
    public UIElement? Described
    {
        get => (UIElement?)GetValue(DescribedProperty);
        set => SetValue(DescribedProperty, value);
    }

    #endregion

    #region Private Methods

    private void Describe()
    {
        Children.Clear();
        if (Described is not { } control)
        {
            return;
        }

        Children.Add(control);
        var words = Styles.Text(Text);
        words.FontSize = Tokens.Number("DvText13");
        words.Foreground = Tokens.Brush(this, "DvText");
        tip.Content = words;
        tip.MaxWidth = Tokens.Number("DvTooltipWidth");
        tip.Padding = Tokens.Space("DvSpace2", "DvSpace1");
        tip.Background = Tokens.Brush(this, "DvBg3");
        tip.BorderBrush = Tokens.Brush(this, "DvLine");
        tip.BorderThickness = new(Tokens.Number("DvBorder"));
        tip.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        ToolTipService.SetToolTip(control, tip);
        AutomationProperties.SetFullDescription(control, Text);
        AutomationProperties.SetHelpText(control, Text);
    }

    #endregion
}
