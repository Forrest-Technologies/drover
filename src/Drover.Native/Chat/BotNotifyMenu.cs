namespace Drover.Native.Chat;

/// <summary>
/// The "More" menu about a bot: what the screen puts in it, then one item that says whether choosing it turns this
/// bot's notifications on or off. With nothing from the screen and the choice not known yet, there is no menu.
/// </summary>
public sealed class BotNotifyMenu : Grid
{
    #region Private Fields

    private readonly Menu menu = new();

    private readonly MenuItem notify = new();

    private readonly BotNotifyMenuViewModel model =
        ActivatorUtilities.CreateInstance<BotNotifyMenuViewModel>(((App)Application.Current).Services);

    private IReadOnlyList<MenuItem> items = [];

    #endregion

    #region Constructors

    public BotNotifyMenu()
    {
        Children.Add(menu);
        notify.Selected += async (_, _) => await model.Flip();
        model.PropertyChanged += (_, _) => Show();
        Loaded += async (_, _) => await Ask();
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<BotNotifyMenu, BotSummary?>(nameof(Bot), null, async menu => await menu.Ask());

    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    /// <summary>What the screen puts in the menu, above the item about notifying. With any, the button is the "more" icon alone.</summary>
    public IReadOnlyList<MenuItem> Items
    {
        get => items;
        set
        {
            items = value;
            model.HasItems = value.Count > 0;
            Show();
        }
    }

    /// <summary>The menu's button, for a layout that shapes it: a phone makes it a finger's width.</summary>
    public PlainButton Trigger => menu.Trigger;

    #endregion

    #region Private Methods

    private async Task Ask()
    {
        if (Bot is { } bot && IsLoaded)
        {
            await model.Show(bot);
        }
    }

    private void Show()
    {
        Visibility = model.HasMenu ? Visibility.Visible : Visibility.Collapsed;
        menu.Label = BotNotifyMenuViewModel.Label;
        menu.AccessibleLabel = model.AccessibleLabel;
        menu.TriggerIcon = items.Count > 0 ? IconName.More : null;
        menu.ShowTriggerLabel = items.Count == 0;

        List<MenuItem> wanted = [.. items];
        if (model.NotifyLabel is { } label)
        {
            notify.Content = label;
            wanted.Add(notify);
        }

        if (!menu.Items.SequenceEqual(wanted))
        {
            menu.Items.Clear();
            foreach (var item in wanted)
            {
                menu.Items.Add(item);
            }
        }
    }

    #endregion
}
