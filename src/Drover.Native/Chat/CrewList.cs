namespace Drover.Native.Chat;

/// <summary>
/// The crew: every bot under the one that manages it. Choosing a bot opens its main conversation. A bot's head is
/// lit while it works for this person, and the connector to a bot holds violet while work handed to it is out,
/// with one pulse as it goes out and one as it comes back.
/// </summary>
public sealed class CrewList : Grid
{
    #region Private Fields

    private readonly StackPanel list = new() { Spacing = Tokens.Number("DvSpace1") };

    private readonly Dictionary<Guid, Row> rows = [];

    private string? shape;

    #endregion

    #region Constructors

    public CrewList()
    {
        Children.Add(new ArrowKeyZone { Items = list });
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CrewProperty =
        Dp.Register<CrewList, IReadOnlyList<CrewNode>>(nameof(Crew), [], crew => crew.Show());

    public static readonly DependencyProperty SelectedIdProperty = Dp.Register<CrewList, Guid?>(nameof(SelectedId), null, crew => crew.Show());

    public static readonly DependencyProperty WorkingProperty =
        Dp.Register<CrewList, IReadOnlySet<Guid>>(nameof(Working), ImmutableHashSet<Guid>.Empty, crew => crew.Show());

    public static readonly DependencyProperty ForYouProperty = Dp.Register<CrewList, IReadOnlyDictionary<Guid, Attention>>(
        nameof(ForYou), ImmutableDictionary<Guid, Attention>.Empty, crew => crew.Show());

    public static readonly DependencyProperty HandoffsProperty = Dp.Register<CrewList, IReadOnlyDictionary<Guid, Handoff>>(
        nameof(Handoffs), ImmutableDictionary<Guid, Handoff>.Empty, crew => crew.Show());

    public IReadOnlyList<CrewNode> Crew
    {
        get => (IReadOnlyList<CrewNode>)GetValue(CrewProperty);
        set => SetValue(CrewProperty, value);
    }

    /// <summary>The bot whose conversation is open.</summary>
    public Guid? SelectedId
    {
        get => (Guid?)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    /// <summary>The bots that are working for this person now.</summary>
    public IReadOnlySet<Guid> Working
    {
        get => (IReadOnlySet<Guid>)GetValue(WorkingProperty);
        set => SetValue(WorkingProperty, value);
    }

    /// <summary>What each bot has for this person. A bot that is not here has nothing.</summary>
    public IReadOnlyDictionary<Guid, Attention> ForYou
    {
        get => (IReadOnlyDictionary<Guid, Attention>)GetValue(ForYouProperty);
        set => SetValue(ForYouProperty, value);
    }

    /// <summary>Work handed to bots in this crew from the conversations on screen, by the bot that has it.</summary>
    public IReadOnlyDictionary<Guid, Handoff> Handoffs
    {
        get => (IReadOnlyDictionary<Guid, Handoff>)GetValue(HandoffsProperty);
        set => SetValue(HandoffsProperty, value);
    }

    #endregion

    #region Private Methods

    // The tree is laid out again only when its shape changed: who is in it, and under whom. Otherwise each row is
    // told what changed and stays where it is, so the focus and a pulse under way are left alone.
    private void Show()
    {
        var flat = ChatRows.CrewRows(Crew);
        var now = string.Join('|', flat.Select(row => $"{row.Bot.Id}:{row.Bot.ParentBotId}:{row.Depth}"));

        if (now != shape)
        {
            shape = now;
            Lay();
        }

        foreach (var (bot, _) in flat)
        {
            rows[bot.Id].Show(bot, bot.Id == SelectedId, Working, ForYou, Handoffs.TryGetValue(bot.Id, out var handoff) ? handoff : null);
        }
    }

    private void Lay()
    {
        var kept = ChatRows.CrewRows(Crew).Select(row => row.Bot.Id).ToHashSet();
        foreach (var removed in rows.Keys.Where(id => !kept.Contains(id)).ToList())
        {
            rows.Remove(removed);
        }

        foreach (var row in rows.Values)
        {
            row.Detach();
        }

        list.Children.Clear();
        foreach (var node in Crew)
        {
            list.Children.Add(Item(node));
        }

        Paint();
    }

    // A bot's row, and under it the bots it manages, beside the connector that runs down from under its head.
    private StackPanel Item(CrewNode node)
    {
        if (!rows.TryGetValue(node.Bot.Id, out var row))
        {
            rows[node.Bot.Id] = row = new();
        }

        var item = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        item.Children.Add(row.Cell);

        if (node.Children.Count > 0)
        {
            var beneath = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
            foreach (var child in node.Children)
            {
                beneath.Children.Add(Item(child));
            }

            item.Children.Add(new Border
            {
                Tag = nameof(CrewList),
                Child = beneath,
                Margin = new(Tokens.Number("DvSpace3") + Tokens.Number("DvSpace4"), 0, 0, 0),
                Padding = new(Tokens.Number("DvSpace2"), 0, 0, 0),
                BorderThickness = new(Tokens.Number("DvBorder"), 0, 0, 0),
            });
        }

        return item;
    }

    private void Paint() => PaintBranches(list);

    private void PaintBranches(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is Border { Tag: nameof(CrewList), Child: Panel beneath } branch)
            {
                branch.BorderBrush = Tokens.Brush(this, "DvLine");
                PaintBranches(beneath);
            }
            else if (child is Panel inner)
            {
                PaintBranches(inner);
            }
        }
    }

    #endregion

    #region Nested Types

    /// <summary>One bot's row: its head, its name, what it has for the person, and its stretch of the connector.</summary>
    private sealed class Row
    {
        #region Private Fields

        private readonly FacetLink link = new();

        private readonly Avatar avatar = new() { Size = 32, VerticalAlignment = VerticalAlignment.Center };

        private readonly TextBlock name = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

        private readonly AttentionNote attention = new();

        private readonly TextBlock paused = Styles.Text("Paused", "DvTextMono");

        private readonly Grid wires = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };

        private Handoff? shownHandoff;

        #endregion

        #region Constructors

        public Row()
        {
            // The name, and what the bot has for the person beside it. When the column is too narrow for both, the
            // word moves under the name instead of cutting the name short.
            var words = new FlowPanel { Gap = Tokens.Number("DvSpace1"), VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(name);
            words.Children.Add(attention);
            AutomationProperties.SetAccessibilityView(name, AccessibilityView.Raw);

            var content = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            SetColumn(words, 1);
            SetColumn(paused, 2);
            paused.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(avatar);
            content.Children.Add(words);
            content.Children.Add(paused);

            link.Content = content;
            link.MinHeight = Targets.Row;
            link.Padding = Tokens.Space("DvSpace3", "DvSpace1");
            ArrowKeyZone.SetIsItem(link, true);
            AutomationProperties.SetAutomationId(link, "CrewRow");

            // The wire lies over the connector beside this row and no further.
            wires.Margin = new(-(Tokens.Number("DvSpace2") + Tokens.Number("DvBorder")), 0, 0, 0);
            wires.Width = Tokens.Number("DvBorder");
            Cell.Children.Add(link);
            Cell.Children.Add(wires);
        }

        #endregion

        #region Properties

        public Grid Cell { get; } = new();

        #endregion

        #region Public Methods

        /// <summary>Takes the row out of the tree it was laid out in, so it can be put in the next one.</summary>
        public void Detach() => (Cell.Parent as Panel)?.Children.Remove(Cell);

        public void Show(
            BotSummary bot,
            bool selected,
            IReadOnlySet<Guid> working,
            IReadOnlyDictionary<Guid, Attention> forYou,
            Handoff? handoff)
        {
            link.Address = Addresses.Talk(bot.Id);
            link.ShowCurrent(selected);
            AutomationProperties.SetName(link, ChatRows.CrewRowName(bot, working, forYou));
            avatar.BotName = bot.Name;
            avatar.Hue = BotIdentity.Hue(bot.Id);
            avatar.State = working.Contains(bot.Id) ? AvatarState.Thinking : AvatarState.Rest;
            name.Text = bot.Name;
            attention.Attention = forYou.GetValueOrDefault(bot.Id);
            paused.Visibility = ChatRows.IsPaused(bot) ? Visibility.Visible : Visibility.Collapsed;
            link.Refresh();
            ShowWire(handoff);
        }

        #endregion

        #region Private Methods

        // The wire is drawn again when the work comes back, so its pulse plays once more and then it goes out of
        // sight. With reduced motion, where nothing plays, work that came back is never seen: the violet while the
        // work is out is the whole signal.
        private void ShowWire(Handoff? handoff)
        {
            if (handoff == shownHandoff)
            {
                return;
            }

            shownHandoff = handoff;
            wires.Children.Clear();

            if (handoff is null)
            {
                return;
            }

            var wire = new LiveLight { State = LiveState.Passing };
            wires.Children.Add(wire);

            if (handoff is Handoff.Returned)
            {
                PulseThenLeave(wire);
            }
        }

        private static void PulseThenLeave(LiveLight wire)
        {
            var beat = Motion.Duration("DvDur3");
            var leave = new DoubleAnimationUsingKeyFrames();
            leave.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = Motion.IsReduced ? 0 : 1 });
            leave.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = beat + beat, Value = 0 });
            Storyboard.SetTarget(leave, wire);
            Storyboard.SetTargetProperty(leave, nameof(Opacity));
            var once = new Storyboard();
            once.Children.Add(leave);
            once.Begin();
        }

        #endregion
    }

    #endregion
}
