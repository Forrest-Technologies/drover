namespace Drover.Native.Session;

/// <summary>
/// The body of the Appearance dialog the account menu opens: the theme, the mode, and, where the theme offers more
/// than its own, the accent. Each is a group of which one is in use. A choice applies at once, and the groups are
/// drawn again from the model with the focus kept on the option that was chosen. The arrow keys move within a group.
/// </summary>
public sealed class AppearanceChoices : StackPanel
{
    #region Private Fields

    private readonly AppearanceViewModel model;

    private readonly Dictionary<string, AppearanceOption> options = [];

    private readonly SpokenWords spoken = new();

    private IReadOnlyList<LookOption> looks = [];

    private IReadOnlyList<ThemeOption> modes = [];

    private IReadOnlyList<AccentOption> accents = [];

    private string? note;

    private string? syncNote;

    private (string Key, FocusState State)? focusOwed;

    #endregion

    #region Constructors

    public AppearanceChoices(AppearanceViewModel model)
    {
        this.model = model;
        Spacing = Tokens.Number("DvSpace5");
        Loaded += (_, _) => Listen(true);
        Unloaded += (_, _) => Listen(false);
        this.WhenShownOrRethemed(Draw);
    }

    #endregion

    #region Properties

    /// <summary>
    /// The option that has the focus, or is owed it. A shell that makes the dialog again after a choice hands this to
    /// the new body, so the focus stays on what was chosen.
    /// </summary>
    public (string Key, FocusState State)? FocusHeld
    {
        get => Focused() ?? focusOwed;
        init => focusOwed = value;
    }

    #endregion

    #region Private Methods

    private void Listen(bool shown)
    {
        model.PropertyChanged -= ModelChanged;
        if (shown)
        {
            model.PropertyChanged += ModelChanged;
        }
    }

    // One change to the theme is told once for each list, and is drawn once.
    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (!looks.SequenceEqual(model.Looks) || !modes.SequenceEqual(model.Themes)
            || !accents.SequenceEqual(model.AccentChoices) || note != model.WaitingNote
            || syncNote != model.SyncNote)
        {
            Draw();
        }
    }

    // Everything is made again, so an option that had the focus is given it back once its replacement is shown.
    private void Draw()
    {
        focusOwed = Focused() ?? focusOwed;
        (looks, modes, accents, note, syncNote) =
            (model.Looks, model.Themes, model.AccentChoices, model.WaitingNote, model.SyncNote);
        options.Clear();
        Children.Clear();

        Children.Add(Group("Theme", Rows(looks.Select(LookRow))));
        Children.Add(Group("Mode", Rows(modes.Select(ModeRow))));

        // A theme with no accent but its own has nothing to choose here.
        if (accents.Count > 1)
        {
            var swatches = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
            foreach (var accent in accents)
            {
                swatches.Children.Add(AccentSwatch(accent));
            }

            Children.Add(Group("Accent", swatches));
        }

        // Said last, so the options do not move under the pointer when it appears.
        if (note is not null)
        {
            Children.Add(Styles.Text(note, "DvTextSecondary"));
        }

        if (syncNote is not null)
        {
            Children.Add(SyncFailure(syncNote));
        }

        Children.Add(spoken);
        spoken.Say(syncNote);

        if (focusOwed is { } owed && !options.ContainsKey(owed.Key))
        {
            focusOwed = null;
        }
    }

    private (string Key, FocusState State)? Focused()
    {
        foreach (var (key, option) in options)
        {
            if (option.FocusState is not FocusState.Unfocused)
            {
                return (key, option.FocusState);
            }
        }

        return null;
    }

    private static StackPanel Group(string heading, UIElement choices)
    {
        var title = Styles.Text(heading, "DvTextLabel");
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);

        var group = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        group.Children.Add(title);
        group.Children.Add(new ArrowKeyZone { Label = heading, Items = choices });
        return group;
    }

    private static StackPanel Rows(IEnumerable<AppearanceOption> rows)
    {
        var list = new StackPanel { Spacing = Tokens.Number("DvSpace1") };
        foreach (var row in rows)
        {
            list.Children.Add(row);
        }

        return list;
    }

    private StackPanel SyncFailure(string sentence)
    {
        var words = new TextBlock
        {
            Text = sentence,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Tokens.Number("DvText12"),
            Foreground = Tokens.Brush(this, "DvText3"),
        };
        var retry = new Button
        {
            Content = "Try again",
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = Tokens.Number("DvText12"),
            Padding = new(Tokens.Number("DvSpace2"), 0, Tokens.Number("DvSpace2"), 0),
        };
        retry.Click += (_, _) => model.RetrySync();

        var line = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        line.Children.Add(words);
        line.Children.Add(retry);
        return line;
    }

    private AppearanceOption LookRow(LookOption look) => Option(
        $"look:{look.Id}",
        Row(look.Name, look.Intent, look.InUse),
        look.InUse,
        look.AccessibleName,
        () => model.ChooseLook(look.Id));

    private AppearanceOption ModeRow(ThemeOption mode) => Option(
        $"mode:{mode.Mode}",
        Row(mode.Name, null, mode.InUse),
        mode.InUse,
        mode.AccessibleName,
        () => model.ChooseTheme(mode.Mode));

    // The swatch is the accent as the mode the dialog is drawn in shows it. Its name stands beside it, and the one in
    // use carries the tick, so neither the accent nor the choice is told by color alone.
    private AppearanceOption AccentSwatch(AccentOption accent)
    {
        var edge = Tokens.Number("DvIcon20");
        var swatch = new Border
        {
            Width = edge,
            Height = edge,
            CornerRadius = new(Tokens.Number("DvRadiusFull")),
            Background = new SolidColorBrush(Shown(accent).ToColor()),
            BorderBrush = Tokens.Brush(this, "DvText3"),
            BorderThickness = new(Tokens.Number("DvBorder")),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        content.Children.Add(swatch);
        content.Children.Add(new TextBlock { Text = accent.Name, VerticalAlignment = VerticalAlignment.Center });
        if (accent.InUse)
        {
            content.Children.Add(Tick());
        }

        var option = Option(
            $"accent:{accent.Id}", content, accent.InUse, accent.AccessibleName, () => model.ChooseAccent(accent.Id));
        option.Line = "DvText3";
        option.HorizontalAlignment = HorizontalAlignment.Left;
        return option;
    }

    private Rgb Shown(AccentOption accent) => ActualTheme is ElementTheme.Light ? accent.Light : accent.Dark;

    // The name, what the choice is for under it where it says so, and the tick's place at the end, kept whether or
    // not the tick is there so the words do not move when the choice does.
    private static Grid Row(string name, string? detail, bool inUse)
    {
        var words = new StackPanel
        {
            Spacing = Tokens.Number("DvSpace1"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        words.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap });
        if (detail is not null)
        {
            words.Children.Add(Styles.Text(detail, "DvTextSecondary"));
        }

        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new(Tokens.Number("DvIcon16")) });
        row.Children.Add(words);

        if (inUse)
        {
            var tick = Tick();
            Grid.SetColumn(tick, 1);
            row.Children.Add(tick);
        }

        return row;
    }

    private static Icon Tick() => new() { NameOfIcon = IconName.Check, VerticalAlignment = VerticalAlignment.Center };

    private AppearanceOption Option(string key, object content, bool inUse, string accessibleName, Action choose)
    {
        var option = new AppearanceOption(inUse)
        {
            Content = content,
            RingInside = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = Tokens.Space("DvSpace3", "DvSpace2"),
            Fill = inUse ? "DvBg3" : null,
            FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary[inUse ? "DvWeightHeading" : "DvWeightLabel"],
        };
        AutomationProperties.SetName(option, accessibleName);
        ArrowKeyZone.SetIsItem(option, true);
        option.Click += (_, _) => choose();

        // After everything shown with it has loaded, so a dialog that puts the focus on its Close button as it opens
        // does not take it back.
        option.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => GiveFocusOwed(key, option));
        options[key] = option;
        return option;
    }

    private void GiveFocusOwed(string key, AppearanceOption option)
    {
        // An option drawn over before its turn came is no longer the one showing.
        var showing = options.TryGetValue(key, out var current) && ReferenceEquals(current, option);
        if (showing && focusOwed is { } owed && owed.Key == key)
        {
            focusOwed = null;
            option.Focus(owed.State);
        }
    }

    // One choice of a group, of which one is in use: a button to press, one of several to assistive technology,
    // which is told whether it is the one in use.
    private sealed class AppearanceOption(bool inUse) : PlainButton
    {
        #region Private Methods

        protected override AutomationPeer OnCreateAutomationPeer() => new ChoicePeer(this, inUse);

        #endregion
    }

    #endregion
}
