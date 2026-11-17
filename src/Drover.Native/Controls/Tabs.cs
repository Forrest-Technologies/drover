namespace Drover.Native.Controls;

/// <summary>
/// Tabs that switch between views of one thing. The showing tab is marked by an underline and by weight. One tab
/// stop: the arrow keys, Home, and End move between tabs and show the one moved to.
/// </summary>
[Microsoft.UI.Xaml.Markup.ContentProperty(Name = nameof(Panel))]
public sealed class Tabs : StackPanel
{
    #region Private Fields

    private readonly StackPanel list = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace4") };

    private readonly Border rule = new();

    private readonly ContentPresenter panel = new() { IsTabStop = false };

    #endregion

    #region Constructors

    public Tabs()
    {
        Spacing = Tokens.Number("DvSpace4");
        rule.Child = new ScrollViewer
        {
            Content = list,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        rule.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));
        Children.Add(rule);
        Children.Add(panel);
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty LabelProperty = Dp.Register<Tabs, string>(nameof(Label), string.Empty, tabs => tabs.Show());

    public static readonly DependencyProperty ItemsProperty = Dp.Register<Tabs, IReadOnlyList<TabItem>>(nameof(Items), [], tabs => tabs.Show());

    public static readonly DependencyProperty ActiveProperty = Dp.Register<Tabs, string?>(nameof(Active), null, tabs => tabs.Show());

    public static readonly DependencyProperty PanelProperty = Dp.Register<Tabs, object?>(nameof(Panel), null, tabs => tabs.Show());

    /// <summary>What the tabs switch between. Read to assistive technology as the tab list's name.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IReadOnlyList<TabItem> Items
    {
        get => (IReadOnlyList<TabItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>The id of the tab that is showing. The first tab shows when this is not set. It can be bound both ways.</summary>
    public string? Active
    {
        get => (string?)GetValue(ActiveProperty);
        set => SetValue(ActiveProperty, value);
    }

    /// <summary>The content under the tabs, when the screen sets it itself as the showing tab changes.</summary>
    public object? Panel
    {
        get => GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    /// <summary>Makes the content for the tab that is showing. Used instead of <see cref="Panel"/> when set.</summary>
    public Func<TabItem, object?>? PanelFor { get; set; }

    private TabItem? ActiveItem => Items.FirstOrDefault(item => item.Id == Active) ?? (Items.Count > 0 ? Items[0] : null);

    #endregion

    #region Private Methods

    private void Show()
    {
        var active = ActiveItem;
        var hadFocus = list.Children.OfType<PlainButton>().Any(tab => tab.FocusState is not FocusState.Unfocused);

        rule.BorderBrush = Tokens.Brush(this, "DvLine");
        AutomationProperties.SetName(list, Label);
        list.Children.Clear();

        foreach (var item in Items)
        {
            var showing = item == active;
            var tab = new TabButton
            {
                Content = item.Label,
                Padding = new(0),
                HoverFill = null,
                Ink = showing ? "DvText" : "DvText2",
                Underline = showing ? "DvText" : null,
                RingInside = true,
                Radius = null,
                IsTabStop = showing,
                FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[showing ? "DvWeightHeading" : "DvWeightLabel"],
            };
            AutomationProperties.SetItemStatus(tab, showing ? "Selected" : string.Empty);
            tab.Click += (_, _) => Activate(item, focus: false);
            tab.KeyDown += (_, key) => Move(item, key);
            list.Children.Add(tab);

            if (showing && hadFocus)
            {
                tab.Loaded += (_, _) => tab.TakeFocus();
            }
        }

        panel.Content = active is not null && PanelFor is not null ? PanelFor(active) : Panel;
        AutomationProperties.SetName(panel, active?.Label ?? string.Empty);
    }

    private void Activate(TabItem item, bool focus)
    {
        if (Active == item.Id)
        {
            return;
        }

        Active = item.Id;
        if (focus)
        {
            list.Children.OfType<PlainButton>().FirstOrDefault(tab => tab.IsTabStop)?.TakeFocus();
        }
    }

    private void Move(TabItem from, KeyRoutedEventArgs key)
    {
        var current = Items.ToList().IndexOf(from);
        int? next = key.Key switch
        {
            VirtualKey.Right => (current + 1) % Items.Count,
            VirtualKey.Left => (current - 1 + Items.Count) % Items.Count,
            VirtualKey.Home => 0,
            VirtualKey.End => Items.Count - 1,
            _ => null,
        };

        if (next is { } index)
        {
            key.Handled = true;
            Activate(Items[index], focus: true);
        }
    }

    private sealed class TabButton : PlainButton
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new RoleButtonPeer(this, AutomationControlType.TabItem);
    }

    #endregion
}
