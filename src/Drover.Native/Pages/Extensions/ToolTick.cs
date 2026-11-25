namespace Drover.Native.Pages;

/// <summary>
/// One suggested tool in an extension's row: a box that is ticked or not, with the tool's name, why the bot would
/// want it, and where it stands. The whole row is the control, so a touch lands anywhere on it. It is a toggle to
/// the platform: a tab stop, turned with Space, announced with its state.
/// </summary>
internal sealed class ToolTick : ToggleButton
{
    #region Private Fields

    private readonly Border box = new();

    private readonly Icon tick = new() { NameOfIcon = IconName.Check, Visibility = Visibility.Collapsed };

    private readonly Border ring = new();

    private bool hasKeyboardFocus;

    #endregion

    #region Constructors

    public ToolTick(string name, string why, string state)
    {
        Template = Styles.Template("DvPlainTemplate");
        UseSystemFocusVisuals = false;
        MinHeight = Targets.Minimum;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Top;
        Padding = new(0);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        box.Width = box.Height = Tokens.Number("DvSpace4");
        box.BorderThickness = new(Tokens.Number("DvBorder"));
        box.CornerRadius = new(Tokens.Number("DvBorder"));
        box.Child = tick;
        box.HorizontalAlignment = HorizontalAlignment.Center;
        box.VerticalAlignment = VerticalAlignment.Center;
        ring.Child = box;
        ring.Padding = new(Tokens.Number("DvFocusOffset"));
        ring.VerticalAlignment = VerticalAlignment.Top;
        ring.Margin = new(0, Tokens.Number("DvSpace1") - Tokens.Number("DvFocusOffset") - Tokens.Number("DvFocusWidth"), 0, 0);

        var words = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        words.Children.Add(Parts.Machine(name, "DvText12", "DvText3"));
        foreach (var line in new[] { why, state }.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            words.Children.Add(Parts.Small(line));
        }

        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(words, 1);
        row.Children.Add(ring);
        row.Children.Add(words);
        Content = row;

        AutomationProperties.SetName(this, string.Join(' ', new[] { name, why, state }.Where(part => !string.IsNullOrWhiteSpace(part))));
        Checked += (_, _) => Paint();
        Unchecked += (_, _) => Paint();
        GotFocus += (_, _) => SetKeyboardFocus(FocusState is FocusState.Keyboard);
        LostFocus += (_, _) => SetKeyboardFocus(false);
        IsEnabledChanged += (_, _) => Paint();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Private Methods

    private void SetKeyboardFocus(bool value)
    {
        hasKeyboardFocus = value;
        Paint();
    }

    private void Paint()
    {
        var on = IsChecked is true;
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            IsEnabled ? Microsoft.UI.Input.InputSystemCursorShape.Hand : Microsoft.UI.Input.InputSystemCursorShape.UniversalNo);
        box.Background = Tokens.Brush(this, on && IsEnabled ? "DvEmber" : "DvBg0");
        box.BorderBrush = Tokens.Brush(this, !IsEnabled ? "DvLine" : on ? "DvEmber" : "DvText3");
        tick.Foreground = Tokens.Brush(this, IsEnabled ? "DvOnAccent" : "DvText3");
        tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ring.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        ring.BorderBrush = Tokens.Brush(this, "DvEmber");
        ring.BorderThickness = new(hasKeyboardFocus ? Tokens.Number("DvFocusWidth") : 0);
        ring.Padding = new(hasKeyboardFocus ? Tokens.Number("DvFocusOffset") : Tokens.Number("DvFocusOffset") + Tokens.Number("DvFocusWidth"));
    }

    #endregion
}
