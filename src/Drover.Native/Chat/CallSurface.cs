namespace Drover.Native.Chat;

/// <summary>
/// A live call with a bot, above its conversation: the bot's head, one word for where the call stands, how long it
/// has run, and Mute, Show words and Hang up. It shrinks to a bar so the conversation stays in use, and the words
/// of the exchange under way are shown only when asked for. A call that did not start says why here, with the way
/// to a model when that is what is missing. Nothing on it moves but the bot's own visor while it thinks.
/// </summary>
public sealed class CallSurface : Grid
{
    #region Private Fields

    private readonly Border box = new();

    private readonly Grid layout = new();

    private readonly Avatar head = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    private readonly TextBlock word = Styles.Text(null, "DvTextH3");

    private readonly TextBlock clock = Styles.Text(null, "DvTextMono");

    private readonly StackPanel state = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock problem = Styles.Text(null, "DvTextSecondary");

    private readonly Link connect = new() { Text = VoiceWords.ConnectOne, Address = Addresses.Models };

    private readonly StackPanel said = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly TextBlock personWords = Styles.Text(null, "DvTextSecondary");

    private readonly TextBlock botWords = Styles.Text(null, "DvTextSecondary");

    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2"), VerticalAlignment = VerticalAlignment.Center };

    private readonly IconButton mute = new() { Icon = IconName.Microphone, Label = VoiceWords.Mute };

    private readonly IconButton words = new() { Icon = IconName.Conversation, Label = VoiceWords.ShowWords };

    private readonly IconButton hangUp = new() { Icon = IconName.PhoneDown, Label = VoiceWords.HangUp, Ink = "DvDanger", HoverInk = "DvDanger" };

    private readonly IconButton shrink = new() { Icon = IconName.Shrink, Label = VoiceWords.ShrinkCall, VerticalAlignment = VerticalAlignment.Top };

    private readonly IconButton close = new() { Icon = IconName.Close, Label = VoiceWords.CloseCall, VerticalAlignment = VerticalAlignment.Top };

    private readonly SpokenWords spoken = new();

    private readonly CallViewModel model;

    // How the parts were last placed: as the bar or the full surface, and which of them showed.
    private string? placedAs;

    #endregion

    #region Constructors

    public CallSurface(CallViewModel model)
    {
        this.model = model;
        word.TextWrapping = clock.TextWrapping = TextWrapping.NoWrap;
        problem.TextWrapping = personWords.TextWrapping = botWords.TextWrapping = TextWrapping.Wrap;
        state.Children.Add(word);
        state.Children.Add(clock);
        said.Children.Add(personWords);
        said.Children.Add(botWords);
        actions.Children.Add(mute);
        actions.Children.Add(words);
        actions.Children.Add(hangUp);

        // Every part is a child of the one layout, which places them as the full surface or as the bar.
        FrameworkElement[] parts = [head, state, problem, connect, said, actions, shrink, close];
        foreach (var part in parts)
        {
            layout.Children.Add(part);
        }

        box.Child = layout;
        box.BorderThickness = new(0, 0, 0, Tokens.Number("DvBorder"));
        Children.Add(box);
        Children.Add(spoken);

        AutomationProperties.SetAutomationId(this, "CallSurface");
        AutomationProperties.SetAutomationId(word, "CallState");
        AutomationProperties.SetAutomationId(mute, "CallMute");
        AutomationProperties.SetAutomationId(words, "CallWords");
        AutomationProperties.SetAutomationId(hangUp, "CallHangUp");
        AutomationProperties.SetAutomationId(shrink, "CallShrink");
        AutomationProperties.SetAutomationId(close, "CallClose");
        AutomationProperties.SetHeadingLevel(word, AutomationHeadingLevel.Level2);

        mute.Click += (_, _) => model.ToggleMute();
        words.Click += (_, _) => model.ToggleWords();
        hangUp.Click += async (_, _) => await model.HangUp();
        shrink.Click += (_, _) => model.ToggleShrunk();
        close.Click += (_, _) => model.Close();
        model.PropertyChanged += (_, _) => Show();
        this.WhenShownOrRethemed(Paint);
        Show();
    }

    #endregion

    #region Private Methods

    private void Show()
    {
        Visibility = Shown(model.Active);

        if (!model.Active || model.Bot is not { } bot)
        {
            spoken.Say(null);
            return;
        }

        var (inCall, current) = (model.InCall, model.State);
        var bar = model.Shrunk && inCall;
        AutomationProperties.SetName(this, VoiceWords.CallWith(bot.Name));

        head.BotName = bot.Name;
        head.Hue = BotIdentity.Hue(bot.Id);
        head.State = current switch
        {
            CallState.Connecting or CallState.Thinking => AvatarState.Thinking,
            CallState.Speaking => AvatarState.Writing,
            _ => AvatarState.Rest,
        };

        word.Text = model.StateWord;
        word.Visibility = Shown(model.StateWord.Length > 0);
        clock.Text = model.Elapsed;
        clock.Visibility = Shown(model.Elapsed.Length > 0);
        state.Visibility = Shown(model.StateWord.Length > 0 || model.Elapsed.Length > 0);
        problem.Text = model.Problem ?? string.Empty;
        problem.Visibility = Shown(model.Problem is not null && !bar);
        connect.Visibility = Shown(model.NeedsModel && !bar);

        actions.Visibility = mute.Visibility = hangUp.Visibility = shrink.Visibility = Shown(inCall);
        words.Visibility = Shown(inCall && !bar);
        close.Visibility = Shown(!inCall);
        mute.Icon = model.Muted ? IconName.MicrophoneOff : IconName.Microphone;
        mute.Label = model.MuteLabel;
        mute.Pressed = model.Muted;
        words.Label = model.WordsLabel;
        words.Pressed = model.WordsShown;
        shrink.Icon = model.Shrunk ? IconName.Expand : IconName.Shrink;
        shrink.Label = model.ShrinkLabel;

        personWords.Text = model.PersonWords.Length > 0 ? $"{VoiceWords.YouSaid}: {model.PersonWords}" : string.Empty;
        personWords.Visibility = Shown(model.PersonWords.Length > 0);
        botWords.Text = model.BotWords.Length > 0 ? $"{VoiceWords.BotSaid(bot.Name)}: {model.BotWords}" : string.Empty;
        botWords.Visibility = Shown(model.BotWords.Length > 0);
        said.Visibility = Shown(inCall && model.WordsShown && !bar && (model.PersonWords.Length > 0 || model.BotWords.Length > 0));

        Place(bar);

        // The state is read out when it changes; the clock is never read out by itself.
        spoken.Say(model.StateWord.Length > 0 ? model.StateWord : model.Problem);
        Paint();
    }

    // The full surface is a column around the bot's head, a row for each part it shows now. The bar is one row:
    // head, state, the actions.
    private void Place(bool bar)
    {
        FrameworkElement[] stacked = [head, state, problem, connect, actions, said];
        var placed = $"{bar}:{string.Concat(stacked.Select(part => part.Visibility is Visibility.Visible ? '1' : '0'))}";

        if (placed == placedAs)
        {
            return;
        }

        placedAs = placed;
        layout.RowDefinitions.Clear();
        layout.ColumnDefinitions.Clear();
        var (auto, star) = (GridLength.Auto, new GridLength(1, GridUnitType.Star));

        if (bar)
        {
            FrameworkElement[] inBar = [head, state, actions, shrink];
            GridLength[] columns = [auto, star, auto, auto];
            foreach (var part in inBar)
            {
                var column = Array.IndexOf(inBar, part);
                layout.ColumnDefinitions.Add(new() { Width = columns[column] });
                SetRow(part, 0);
                SetColumn(part, column);
                SetColumnSpan(part, 1);
            }

            layout.ColumnSpacing = Tokens.Number("DvSpace2");
            layout.RowSpacing = 0;
            box.Padding = Tokens.Space("DvSpace2", "DvSpace2");
            head.Size = (int)Tokens.Number("DvAvatar32");
            state.Orientation = Orientation.Horizontal;
            state.Spacing = Tokens.Number("DvSpace2");
            state.HorizontalAlignment = word.HorizontalAlignment = clock.HorizontalAlignment = HorizontalAlignment.Left;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            shrink.VerticalAlignment = clock.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            layout.ColumnDefinitions.Add(new() { Width = star });
            layout.ColumnDefinitions.Add(new() { Width = auto });
            layout.ColumnSpacing = 0;
            layout.RowSpacing = Tokens.Number("DvSpace3");
            box.Padding = Tokens.Space("DvSpace4", "DvSpace4");
            head.Size = (int)Tokens.Number("DvAvatar96");
            state.Orientation = Orientation.Vertical;
            state.Spacing = Tokens.Number("DvSpace1");
            state.HorizontalAlignment = word.HorizontalAlignment = clock.HorizontalAlignment = HorizontalAlignment.Center;
            problem.HorizontalAlignment = connect.HorizontalAlignment = actions.HorizontalAlignment = HorizontalAlignment.Center;
            problem.TextAlignment = TextAlignment.Center;
            shrink.VerticalAlignment = close.VerticalAlignment = VerticalAlignment.Top;

            // Each part showing gets a row, centered over the whole width, so the button in the corner does not
            // push it off the middle and a part not showing leaves no gap.
            foreach (var part in stacked)
            {
                var row = Math.Max(0, layout.RowDefinitions.Count - 1);

                if (part.Visibility is Visibility.Visible)
                {
                    row = layout.RowDefinitions.Count;
                    layout.RowDefinitions.Add(new() { Height = auto });
                }

                SetRow(part, row);
                SetColumn(part, 0);
                SetColumnSpan(part, layout.ColumnDefinitions.Count);
            }

            foreach (var corner in (FrameworkElement[])[shrink, close])
            {
                SetRow(corner, 0);
                SetColumn(corner, layout.ColumnDefinitions.Count - 1);
                SetColumnSpan(corner, 1);
            }
        }

        // The parts moved inside the surface. In a browser the tree for assistive technology places a part only when
        // the part itself moves, so the surface's parts are put in it again where they now stand (see SemanticTree).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => SemanticTree.PutBack(layout));
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void Paint()
    {
        box.Background = Tokens.Brush(this, "DvBg1");
        box.BorderBrush = Tokens.Brush(this, "DvLine");
        clock.Foreground = Tokens.Brush(this, "DvText2");
        problem.Foreground = Tokens.Brush(this, model.State is CallState.NotStarted ? "DvText" : "DvText2");
    }

    #endregion
}
