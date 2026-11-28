namespace Drover.Native.Controls;

/// <summary>
/// A button that opens a short list of actions. The list is the platform's own flyout: it closes on Escape and on
/// a press outside it, and puts the focus back on the button. The arrow keys, Home, and End move between the
/// actions, and the first one that can be taken has the focus when the list opens.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Items))]
public sealed class Menu : Grid
{
    #region Private Fields

    private readonly PlainButton trigger = new() { Fill = "DvBg2", Line = "DvText3" };

    private readonly Icon icon = new() { Size = 20 };

    private readonly TextBlock words = new();

    private readonly StackPanel list = new();

    private readonly Flyout flyout = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

    private MenuItem? afterClose;

    private Pressable? opener;

    #endregion

    #region Constructors

    public Menu()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(icon);
        content.Children.Add(words);
        trigger.Content = content;
        trigger.Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), 0);
        trigger.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        trigger.Click += (_, _) => OpenAt(trigger);
        Children.Add(trigger);

        flyout.Content = list;
        flyout.Opened += (_, _) => FirstOpen()?.TakeFocus();
        flyout.Closed += (_, _) =>
        {
            if (afterClose is { } item)
            {
                afterClose = null;
                DispatcherQueue.TryEnqueue(() => Act(item));
            }
        };
        list.KeyDown += (_, key) => Move(key);
        Items.CollectionChanged += (_, _) => Describe();
        Describe();
        this.WhenShownOrRethemed(Describe);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty = Dp.Register<Menu, string>(nameof(Label), string.Empty, menu => menu.Describe());

    public static readonly DependencyProperty AccessibleLabelProperty =
        Dp.Register<Menu, string?>(nameof(AccessibleLabel), null, menu => menu.Describe());

    public static readonly DependencyProperty TriggerIconProperty =
        Dp.Register<Menu, IconName?>(nameof(TriggerIcon), null, menu => menu.Describe());

    public static readonly DependencyProperty ShowTriggerLabelProperty =
        Dp.Register<Menu, bool>(nameof(ShowTriggerLabel), true, menu => menu.Describe());

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>A fuller name for the button when its label alone is ambiguous. It starts with the label.</summary>
    public string? AccessibleLabel
    {
        get => (string?)GetValue(AccessibleLabelProperty);
        set => SetValue(AccessibleLabelProperty, value);
    }

    public IconName? TriggerIcon
    {
        get => (IconName?)GetValue(TriggerIconProperty);
        set => SetValue(TriggerIconProperty, value);
    }

    public bool ShowTriggerLabel
    {
        get => (bool)GetValue(ShowTriggerLabelProperty);
        set => SetValue(ShowTriggerLabelProperty, value);
    }

    /// <summary>The actions, in the order shown.</summary>
    public System.Collections.ObjectModel.ObservableCollection<MenuItem> Items { get; } = [];

    /// <summary>The menu's button, for a layout that shapes it: the dock of a phone stacks its icon over its label.</summary>
    public PlainButton Trigger => trigger;

    /// <summary>Where the list opens relative to its trigger.</summary>
    public FlyoutPlacementMode Placement
    {
        get => flyout.Placement;
        set => flyout.Placement = value;
    }

    #endregion

    #region Public Methods

    /// <summary>Puts the focus on the menu's button: where it belongs after something the menu opened has closed.</summary>
    public void ReturnFocusToTrigger() => trigger.TakeFocus();

    #endregion

    #region Internal Methods

    /// <summary>Opens the actions from a control that has its own layout and accessible name.</summary>
    internal void OpenAt(Pressable anchor)
    {
        opener = anchor;
        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.BackgroundProperty, Tokens.Brush(anchor, "DvBg2")));
        presenter.Setters.Add(new Setter(Control.BorderBrushProperty, Tokens.Brush(anchor, "DvLine")));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(Tokens.Number("DvBorder"))));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.Number("DvRadiusCard"))));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Tokens.Number("DvSpace1"))));
        presenter.Setters.Add(new Setter(MinWidthProperty, Tokens.Number("DvMenuWidth")));
        presenter.Setters.Add(new Setter(MinHeightProperty, 0d));
        presenter.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        flyout.FlyoutPresenterStyle = presenter;
        list.RequestedTheme = anchor.ActualTheme;
        flyout.ShowAt(anchor);
    }

    #endregion

    #region Private Methods

    private void Describe()
    {
        icon.Visibility = TriggerIcon is null ? Visibility.Collapsed : Visibility.Visible;
        icon.NameOfIcon = TriggerIcon ?? IconName.More;
        icon.Foreground = trigger.CurrentInk;
        words.Text = Label;
        words.Visibility = ShowTriggerLabel ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(trigger, AccessibleLabel ?? Label);
        AutomationProperties.SetName(list, Label);

        list.Children.Clear();
        foreach (var item in Items)
        {
            item.Click -= Choose;
            item.Click += Choose;
            list.Children.Add(item);
        }
    }

    // The menu closes and the focus goes back to its button first, so a dialog the action opens remembers the
    // button as the place to return to.
    private void Choose(object sender, RoutedEventArgs args)
    {
        var item = (MenuItem)sender;
        if (item.OpensFlyout)
        {
            afterClose = item;
            flyout.Hide();
            return;
        }

        flyout.Hide();
        Act(item);
    }

    private void Act(MenuItem item)
    {
        (opener ?? trigger).TakeFocus();
        item.RaiseSelected();
    }

    private MenuItem? FirstOpen() => Items.FirstOrDefault(item => item.IsEnabled);

    private void Move(KeyRoutedEventArgs key)
    {
        MenuItem[] open = [.. Items.Where(item => item.IsEnabled)];
        var index = Array.FindIndex(open, item => item.FocusState is not FocusState.Unfocused);
        var target = key.Key switch
        {
            _ when open.Length == 0 => null,
            VirtualKey.Down => open[(index + 1) % open.Length],
            VirtualKey.Up => open[(index - 1 + open.Length) % open.Length],
            VirtualKey.Home => open[0],
            VirtualKey.End => open[^1],
            _ => null,
        };

        if (target is not null)
        {
            key.Handled = true;
            target.TakeFocus();
        }
    }

    #endregion
}
