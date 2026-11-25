namespace Drover.Native.Pages;

/// <summary>
/// One bot in the list, then the bots it manages, set in beneath it behind a thin connector. The card opens the bot;
/// its name is the link a keyboard and a screen reader find, and the focus ring goes around the card.
/// </summary>
public sealed class CrewRow : StackPanel
{
    #region Private Fields

    private readonly ListCard card = new();

    #endregion

    #region Constructors

    public CrewRow(BotListViewModel list, CrewNode node, Action<string> open)
    {
        var bot = node.Bot;
        card.Leading = new Avatar
        {
            BotName = bot.Name,
            Hue = BotListViewModel.Hue(bot),
            Size = 48,
            VerticalAlignment = VerticalAlignment.Center,
        };
        card.Title = bot.Name;
        card.Facts = list.Facts(bot);

        if (bot.Status is "paused")
        {
            card.Status = new Badge { Text = BotListViewModel.PausedBadge, Kind = BadgeKind.Warn };
        }

        if (list.ShowsAddUnder)
        {
            var add = new IconButton { Icon = IconName.Plus, Label = BotListViewModel.AddUnderLabel(bot), VerticalAlignment = VerticalAlignment.Center };
            add.Click += (_, _) => list.Create(bot.Id);
            card.Trailing = add;
        }

        if (list.CanEdit)
        {
            card.Opened += () => open(Addresses.Bot(bot.Id));
        }

        if (node.Children.Count > 0)
        {
            card.Beneath = Beneath(list, node, open);
        }

        Children.Add(card);
    }

    #endregion

    #region Private Methods

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

    #endregion
}
