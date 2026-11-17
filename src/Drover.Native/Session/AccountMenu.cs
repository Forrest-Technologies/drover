namespace Drover.Native.Session;

/// <summary>
/// The account menu: the theme, the ways to the settings screens, and signing out. In the bar it carries the
/// person's name; compact, in a phone's dock, it is an icon over the word "Account".
/// </summary>
public sealed class AccountMenu : Grid
{
    #region Private Fields

    private readonly AccountMenuViewModel model = ActivatorUtilities.CreateInstance<AccountMenuViewModel>(((App)Application.Current).Services);

    private readonly Menu menu = new();

    private readonly Dialog notifications = new() { Title = "Notifications on this device" };

    #endregion

    #region Constructors

    public AccountMenu()
    {
        notifications.Body = new StatusNote
        {
            Text = "This app cannot turn notifications on yet. Turn them on in the web app, in this same menu.",
        };
        notifications.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => NotificationsChanged());
        Children.Add(menu);
        Children.Add(notifications);

        model.PropertyChanged += (_, _) => Show();
        Unloaded += (_, _) => model.Dispose();
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CompactProperty =
        Dp.Register<AccountMenu, bool>(nameof(Compact), false, menu => menu.model.Compact = menu.Compact);

    /// <summary>For a phone's dock: an icon over the word, in place of the person's name on a button.</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        menu.Label = model.Label;
        menu.AccessibleLabel = model.AccessibleLabel;
        menu.TriggerIcon = Compact ? IconName.Account : null;
        menu.HorizontalAlignment = Compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        notifications.Open = model.NotificationsOpen;

        if (Compact)
        {
            ShapeForDock();
        }

        menu.Items.Clear();
        foreach (var option in model.Themes)
        {
            menu.Items.Add(ThemeItem(option));
        }

        menu.Items.Add(Item("Models", () => model.Open(Addresses.Models)));
        menu.Items.Add(Item("Extensions", () => model.Open(Addresses.Extensions)));
        menu.Items.Add(Item("Notifications", model.OpenNotifications));

        // Everyone reads usage: a member reads their own runs, an owner or admin the whole space.
        menu.Items.Add(Item("Usage", () => model.Open(Addresses.Usage)));
        if (model.ShowsStatus)
        {
            menu.Items.Add(Item("Status", () => model.Open(Addresses.Status)));
        }

        menu.Items.Add(Item("Sign out", () => _ = model.SignOut()));
    }

    // In the dock the menu's button is one of four equal cells: no surface, no line, the icon over the word.
    private void ShapeForDock()
    {
        var trigger = menu.Trigger;
        (trigger.Fill, trigger.HoverFill, trigger.Line, trigger.Ink, trigger.Radius, trigger.RingInside) =
            (null, null, null, "DvText2", "DvBorder", true);
        trigger.MinHeight = Tokens.Number("DvTargetTouch") + Tokens.Number("DvSpace3");
        trigger.FontSize = Tokens.Number("DvText12");
        trigger.HorizontalAlignment = HorizontalAlignment.Stretch;

        if (trigger.Content is StackPanel content)
        {
            content.Orientation = Orientation.Vertical;
            content.Spacing = Tokens.Number("DvSpace1");
            foreach (var part in content.Children.OfType<FrameworkElement>())
            {
                part.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
    }

    private static MenuItem Item(string label, Action choose)
    {
        var item = new MenuItem { Content = label, HorizontalContentAlignment = HorizontalAlignment.Left };
        item.Selected += (_, _) => choose();
        return item;
    }

    // The theme in use carries a tick, and says so in words.
    private MenuItem ThemeItem(ThemeOption option)
    {
        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = option.Name, VerticalAlignment = VerticalAlignment.Center });

        if (option.InUse)
        {
            var tick = new Icon { NameOfIcon = IconName.Check, VerticalAlignment = VerticalAlignment.Center, Foreground = Tokens.Brush(this, "DvText") };
            SetColumn(tick, 1);
            row.Children.Add(tick);
        }

        var item = new MenuItem { Content = row };
        AutomationProperties.SetName(item, option.AccessibleName);
        item.Selected += (_, _) => model.ChooseTheme(option.Choice);
        return item;
    }

    // The item that opened the dialog is in a closed menu, so the focus goes back to the menu's own button.
    private void NotificationsChanged()
    {
        if (!notifications.Open && model.NotificationsOpen)
        {
            model.NotificationsOpen = false;
            menu.ReturnFocusToTrigger();
        }
    }

    #endregion
}
