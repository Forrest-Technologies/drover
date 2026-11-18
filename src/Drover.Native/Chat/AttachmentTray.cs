namespace Drover.Native.Chat;

/// <summary>
/// The files chosen for a message, above the composer's field. Each says its state in words and can be taken
/// off. Nothing is shown when there is no file.
/// </summary>
public sealed class AttachmentTray : StackPanel
{
    #region Private Fields

    private readonly FlowPanel chips = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly SpokenWords spoken = new();

    #endregion

    #region Constructors

    public AttachmentTray()
    {
        AutomationProperties.SetName(chips, "Files attached to this message");
        Children.Add(chips);
        Children.Add(spoken);
        this.WhenShownOrRethemed(Show);
        Show();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty ItemsProperty =
        Dp.Register<AttachmentTray, IReadOnlyList<AttachmentItem>>(nameof(Items), [], tray => tray.Show());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<AttachmentTray, string>(nameof(BotName), string.Empty, tray => tray.Show());

    public IReadOnlyList<AttachmentItem> Items
    {
        get => (IReadOnlyList<AttachmentItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>Raised with a file's id when it is taken off before sending.</summary>
    public event EventHandler<Guid>? Remove;

    #endregion

    #region Private Methods

    private void Show()
    {
        Visibility = Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // A chip is kept while its file is listed, so the focus on its button outlives a change of the words beside it.
        var kept = chips.Children.OfType<Facet>().ToDictionary(chip => (Guid)chip.Tag);
        chips.Children.Clear();

        foreach (var item in Items)
        {
            var chip = kept.GetValueOrDefault(item.Id) ?? Chip(item);
            Describe(chip, item);
            chips.Children.Add(chip);
        }

        // Progress is announced when a file finishes or fails, not percent by percent.
        spoken.Say(Items.Count > 0 ? ChatRows.Announced(Items) : null);
    }

    private Facet Chip(AttachmentItem item)
    {
        var name = Styles.Text(item.Name);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightLabel"];
        var status = Styles.Text(null, "DvTextMono");
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(name);
        words.Children.Add(status);

        var remove = new IconButton { Icon = IconName.Close, Label = $"Remove {item.Name}" };
        remove.Click += (_, _) => Remove?.Invoke(this, item.Id);

        var chip = new Facet
        {
            Tag = item.Id,
            Cut = Tokens.Number("DvCutS"),
            Padding = new(Tokens.Number("DvSpace3"), Tokens.Number("DvSpace1"), Tokens.Number("DvSpace1"), Tokens.Number("DvSpace1")),
            ColumnSpacing = Tokens.Number("DvSpace2"),
        };
        chip.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        chip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(remove, 1);
        chip.Children.Add(words);
        chip.Children.Add(remove);
        return chip;
    }

    // A file that cannot go: framed in the failure color, and said in words under its name.
    private void Describe(Facet chip, AttachmentItem item)
    {
        var problem = ChatRows.HasProblem(item);
        var status = (TextBlock)((StackPanel)chip.Children[1]).Children[1];
        status.Text = FileWords.Status(item, BotName);
        status.Foreground = Tokens.Brush(this, problem ? "DvDanger" : "DvText3");
        chip.Fill = Tokens.Brush(this, "DvBg2");
        chip.Edge = problem ? Tokens.Brush(this, "DvDanger") : null;
    }

    #endregion
}
