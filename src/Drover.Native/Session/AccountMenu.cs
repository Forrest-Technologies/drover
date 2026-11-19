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

    private readonly Dialog? updates;

    private readonly TextBlock? updateDetail;

    private readonly Switch? updateChecks;

    private readonly Button? checkUpdate;

    private readonly Button? installUpdate;

    private bool refreshingUpdates;

    #endregion

    #region Constructors

    public AccountMenu()
    {
        notifications.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => NotificationsChanged());
        Children.Add(menu);
        Children.Add(notifications);

        // A build without the update key shows no update controls. The dialog stays out of the tree, including the
        // phone dock.
        if (model.ShowsUpdates)
        {
            updates = new Dialog();
            updateDetail = Styles.Text(null, "DvTextParagraph");
            updateChecks = new Switch { Label = "Check for desktop updates" };
            checkUpdate = new Button { Content = "Check now" };
            installUpdate = new Button { Content = model.InstallLabel, Kind = ButtonKind.Primary };
            updates.RegisterPropertyChangedCallback(Dialog.OpenProperty, (_, _) => UpdatesChanged());
            updateChecks.RegisterPropertyChangedCallback(Switch.ValueProperty, (_, _) => UpdateChecksChanged());
            checkUpdate.Click += (_, _) => _ = model.CheckUpdates();
            installUpdate.Click += (_, _) => _ = model.InstallUpdate();

            var updateBody = new StackPanel { Spacing = Tokens.Number("DvSpace4") };
            updateBody.Children.Add(updateDetail);
            updateBody.Children.Add(updateChecks);
            var updateActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
            updateActions.Children.Add(checkUpdate);
            updateActions.Children.Add(installUpdate);
            updates.Body = updateBody;
            updates.Actions = updateActions;
            Children.Add(updates);
        }
        else
        {
            updates = null;
            updateDetail = null;
            updateChecks = null;
            checkUpdate = null;
            installUpdate = null;
        }

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
        ShowNotifications();
        ShowUpdates();

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
        if (model.ShowsUpdates)
        {
            menu.Items.Add(Item(model.UpdatesLabel, model.OpenUpdates));
        }

        // Everyone reads usage: a member reads their own runs, an owner or admin the whole space.
        menu.Items.Add(Item("Usage", () => model.Open(Addresses.Usage)));
        if (model.ShowsStatus)
        {
            menu.Items.Add(Item("Status", () => model.Open(Addresses.Status)));
        }

        menu.Items.Add(Item("Sign out", () => _ = model.SignOut()));
    }

    // The control is made each time the dialog opens, so the device is asked afresh, and let go when it closes.
    private void ShowNotifications()
    {
        if (model.NotificationsOpen && !notifications.Open)
        {
            notifications.Body = new DevicePush { TitleHidden = true };
        }

        notifications.Open = model.NotificationsOpen;

        if (!model.NotificationsOpen)
        {
            notifications.Body = null;
        }
    }

    private void ShowUpdates()
    {
        if (updates is null || updateDetail is null || updateChecks is null || checkUpdate is null
            || installUpdate is null)
        {
            return;
        }

        updates.Title = model.UpdatesTitle;
        updateDetail.Text = model.UpdatesDetail;
        refreshingUpdates = true;
        updateChecks.Value = model.UpdateChecksEnabled;
        refreshingUpdates = false;
        checkUpdate.IsEnabled = model.CanCheckUpdates;
        installUpdate.Content = model.InstallLabel;
        installUpdate.IsEnabled = model.CanInstallUpdate;
        installUpdate.Visibility = model.CanInstallUpdate ? Visibility.Visible : Visibility.Collapsed;
        updates.Open = model.UpdatesOpen;
    }

    // In the dock the menu's button is one of four equal cells: no surface, no line, the icon over the word.
    private void ShapeForDock()
    {
        var trigger = menu.Trigger;
        (trigger.Fill, trigger.HoverFill, trigger.Line, trigger.Ink, trigger.Radius, trigger.RingInside) =
            (null, null, null, "DvText2", null, true);
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

    private void UpdatesChanged()
    {
        if (updates is not null && !updates.Open && model.UpdatesOpen)
        {
            model.UpdatesOpen = false;
            menu.ReturnFocusToTrigger();
        }
    }

    private void UpdateChecksChanged()
    {
        if (!refreshingUpdates && updateChecks is not null)
        {
            model.SetUpdateChecks(updateChecks.Value);
        }
    }

    #endregion
}
