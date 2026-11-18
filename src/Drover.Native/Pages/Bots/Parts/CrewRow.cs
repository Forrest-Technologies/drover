namespace Drover.Native.Pages;

/// <summary>
/// One bot in the list, then the bots it manages, set in beneath it behind a thin connector. The whole row opens
/// the bot; its name is the link a keyboard and a screen reader find, and the focus ring goes around the row.
/// </summary>
public sealed class CrewRow : StackPanel
{
    #region Private Fields

    private readonly Facet frame = new();

    private bool hovered;

    private bool focused;

    #endregion

    #region Constructors

    public CrewRow(BotListViewModel list, CrewNode node, Action<string> open)
    {
        var bot = node.Bot;
        Spacing = Tokens.Number("DvSpace3");
        frame.Cut = Tokens.Number("DvCut");
        frame.MinHeight = Tokens.Number("DvTargetTouch");
        frame.Padding = Tokens.Space("DvSpace4", "DvSpace3");

        var line = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var avatar = new Avatar { BotName = bot.Name, Hue = BotListViewModel.Hue(bot), Size = 48, VerticalAlignment = VerticalAlignment.Center };
        var text = BotParts.Column("DvSpace1", NameOf(list, bot, open), BotParts.Mono(list.Facts(bot)));
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        line.Children.Add(avatar);
        line.Children.Add(text);

        if (bot.Status is "paused")
        {
            var paused = new Badge { Text = BotListViewModel.PausedBadge, Kind = BadgeKind.Warn };
            Grid.SetColumn(paused, 2);
            line.Children.Add(paused);
        }

        if (list.ShowsAddUnder)
        {
            var add = new IconButton { Icon = IconName.Plus, Label = BotListViewModel.AddUnderLabel(bot), IsEnabled = !list.Creating, VerticalAlignment = VerticalAlignment.Center };
            add.Click += (_, _) => _ = list.Create(bot.Id);
            Grid.SetColumn(add, 3);
            line.Children.Add(add);
        }

        frame.Children.Add(line);
        Children.Add(frame);

        if (list.CanEdit)
        {
            frame.PointerEntered += (_, _) => Hover(true);
            frame.PointerExited += (_, _) => Hover(false);
            frame.Tapped += (_, args) =>
            {
                if (!Within<IconButton>(args.OriginalSource as DependencyObject))
                {
                    open(Addresses.Bot(bot.Id));
                }
            };
        }

        if (node.Children.Count > 0)
        {
            Children.Add(Beneath(list, node, open));
        }

        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Private Methods

    private UIElement NameOf(BotListViewModel list, BotSummary bot, Action<string> open)
    {
        var words = Styles.Text(bot.Name, "DvTextH3");
        AutomationProperties.SetHeadingLevel(words, AutomationHeadingLevel.None);

        if (!list.CanEdit)
        {
            return words;
        }

        var link = new RowLink { Content = words };
        AutomationProperties.SetName(link, bot.Name);
        link.Click += (_, _) => open(Addresses.Bot(bot.Id));
        link.KeyboardFocus += value =>
        {
            focused = value;
            Paint();
        };
        return link;
    }

    // The bots a bot manages sit in beneath it, joined by the same thin connector the crew list uses.
    private static Border Beneath(BotListViewModel list, CrewNode node, Action<string> open)
    {
        var beneath = new NamedGroup(BotListViewModel.BeneathName(node.Bot), "DvSpace3") { Role = AutomationControlType.List };
        foreach (var child in node.Children)
        {
            beneath.Children.Add(new CrewRow(list, child, open));
        }

        var connector = new Border
        {
            Child = beneath,
            Margin = new(Tokens.Number("DvSpace4"), 0, 0, 0),
            Padding = new(Tokens.Number("DvSpace4"), 0, 0, 0),
            BorderThickness = new(Tokens.Number("DvBorder"), 0, 0, 0),
        };
        connector.WhenShownOrRethemed(() => connector.BorderBrush = Tokens.Brush(connector, "DvLine"));
        return connector;
    }

    private void Hover(bool value)
    {
        hovered = value;
        Paint();
    }

    private void Paint()
    {
        frame.Edge = Tokens.Brush(this, hovered ? "DvText3" : "DvLine");
        frame.Fill = Tokens.Brush(this, hovered ? "DvBg2" : "DvBg1");
        frame.Ring = focused ? Tokens.Brush(this, "DvEmber") : null;
    }

    private static bool Within<T>(DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
