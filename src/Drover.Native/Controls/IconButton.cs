namespace Drover.Native.Controls;

/// <summary>
/// A button that shows only an icon. What it does is said in <see cref="Label"/>, which is its accessible name and
/// its tooltip, so it is required: an icon alone has no name.
/// </summary>
public sealed class IconButton : PlainButton
{
    #region Private Fields

    private readonly Icon icon = new() { Size = 20 };

    #endregion

    #region Constructors

    public IconButton()
    {
        Content = icon;
        Padding = new(0);
        Ink = "DvText2";
        Width = Height = Targets.Minimum;
        Loaded += (_, _) => RequireLabel();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty IconProperty =
        Dp.Register<IconButton, IconName>(nameof(Icon), IconName.Info, button => button.icon.NameOfIcon = button.Icon);

    public static readonly DependencyProperty LabelProperty =
        Dp.Register<IconButton, string>(nameof(Label), string.Empty, button => button.ApplyLabel());

    public IconName Icon
    {
        get => (IconName)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>What the control does, in words. It is the accessible name, so it is required.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    #endregion

    #region Private Methods

    protected override void Painted() => icon.Foreground = CurrentInk;

    private void ApplyLabel()
    {
        AutomationProperties.SetName(this, Label);
        ToolTipService.SetToolTip(this, Label);
    }

    private void RequireLabel()
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"{nameof(IconButton)} needs a {nameof(Label)}: an icon alone has no name.");
        }
    }

    #endregion
}
