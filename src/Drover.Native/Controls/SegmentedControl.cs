namespace Drover.Native.Controls;

/// <summary>
/// A few choices side by side, one of them taken. The chosen one is marked by surface and by weight; one that
/// cannot be taken is dimmed and struck through. One tab stop: the arrow keys move the choice.
/// </summary>
public sealed class SegmentedControl : Grid
{
    #region Private Fields

    private readonly Border box = new();

    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal };

    #endregion

    #region Constructors

    public SegmentedControl()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        box.Child = row;
        box.BorderThickness = new(Tokens.Number("DvBorder"));
        box.CornerRadius = new(Tokens.Number("DvRadiusControl"));
        Children.Add(box);
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty =
        Dp.Register<SegmentedControl, string>(nameof(Label), string.Empty, control => control.Show());

    public static readonly DependencyProperty OptionsProperty =
        Dp.Register<SegmentedControl, IReadOnlyList<SegmentOption<string>>>(nameof(Options), [], control => control.Show());

    public static readonly DependencyProperty ValueProperty =
        Dp.Register<SegmentedControl, string?>(nameof(Value), null, control => control.Show());

    public static readonly DependencyProperty DisabledProperty =
        Dp.Register<SegmentedControl, bool>(nameof(Disabled), false, control => control.Show());

    public static readonly DependencyProperty FillsProperty =
        Dp.Register<SegmentedControl, bool>(nameof(Fills), false, control => control.Show());

    /// <summary>What is being chosen. Read to assistive technology as the group's name.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The choices. A value is text here: a screen that chooses between enum members passes their names.</summary>
    public IReadOnlyList<SegmentOption<string>> Options
    {
        get => (IReadOnlyList<SegmentOption<string>>)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>The value of the chosen option. It can be bound both ways.</summary>
    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool Disabled
    {
        get => (bool)GetValue(DisabledProperty);
        set => SetValue(DisabledProperty, value);
    }

    /// <summary>Spans the width it is given, each choice the same width, as the provider choice of a setup form does.</summary>
    public bool Fills
    {
        get => (bool)GetValue(FillsProperty);
        set => SetValue(FillsProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        var hadFocus = row.Children.OfType<PlainButton>().Any(segment => segment.FocusState is not FocusState.Unfocused);
        var tabStop = Options.FirstOrDefault(option => option.Value == Value) ?? Options.FirstOrDefault(option => !option.Disabled);

        HorizontalAlignment = Fills ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        box.BorderBrush = Tokens.Brush(this, "DvText3");
        AutomationProperties.SetName(row, Label);
        row.Children.Clear();

        foreach (var option in Options)
        {
            var chosen = option.Value == Value;
            var segment = new SegmentButton
            {
                Content = Words(option, chosen),
                Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0),
                Fill = chosen ? "DvBg3" : null,
                HoverFill = chosen ? "DvBg3" : null,
                Ink = chosen ? "DvText" : "DvText2",
                HoverInk = chosen ? "DvText" : "DvText2",
                RingInside = true,
                Radius = null,
                IsEnabled = !Disabled && !option.Disabled,
                IsTabStop = option == tabStop,
                FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[chosen ? "DvWeightHeading" : "DvWeightLabel"],
            };
            AutomationProperties.SetName(segment, option.Label);
            AutomationProperties.SetItemStatus(segment, chosen ? "Selected" : string.Empty);
            segment.Click += (_, _) => Value = option.Value;
            segment.KeyDown += (_, key) => Move(option, key);

            row.Children.Add(new Border
            {
                Child = segment,
                BorderBrush = Tokens.Brush(this, "DvLine"),
                BorderThickness = new(row.Children.Count == 0 ? 0 : Tokens.Number("DvBorder"), 0, 0, 0),
            });

            if (chosen && hadFocus)
            {
                segment.Loaded += (_, _) => segment.TakeFocus();
            }
        }
    }

    // A choice that cannot be taken is struck through, so it is not told by color alone.
    private static TextBlock Words(SegmentOption<string> option, bool chosen) => new()
    {
        Text = option.Label,
        TextDecorations = option.Disabled && !chosen ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
    };

    private void Move(SegmentOption<string> from, KeyRoutedEventArgs key)
    {
        List<SegmentOption<string>> open = [.. Options.Where(option => !option.Disabled)];
        var current = open.IndexOf(from);
        int? next = key.Key switch
        {
            VirtualKey.Right or VirtualKey.Down => (current + 1) % open.Count,
            VirtualKey.Left or VirtualKey.Up => (current - 1 + open.Count) % open.Count,
            _ => null,
        };

        if (next is { } index && open.Count > 0)
        {
            key.Handled = true;
            Value = open[index].Value;
        }
    }

    private sealed class SegmentButton : PlainButton
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.RadioButton);
    }

    #endregion
}
