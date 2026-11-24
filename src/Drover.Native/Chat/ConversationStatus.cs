namespace Drover.Native.Chat;

/// <summary>
/// The one quiet line under a conversation's composer: text only, its parts joined by a middle dot. While a run
/// works it says what the bot is doing, in the color of a bot at work, then the model. At rest it names the model
/// that wrote the newest reply, then the tokens and the cost of that reply, the numbers in the machine font. When it
/// has usage to open it is one button that opens the popover of it; otherwise it is plain text. It never wraps and
/// never moves: where the line is too narrow it shows the leading parts that fit, and the first is cut short if it
/// alone is too long. The whole sentence is its accessible name. With nothing to say the line takes no room.
/// </summary>
public sealed class ConversationStatus : Grid
{
    #region Private Fields

    private readonly TextBlock words = Line();

    private readonly PlainButton button = new()
    {
        Ink = "DvText3",
        HoverInk = "DvText2",
        HoverFill = null,
        Radius = null,
        MinHeight = Targets.Minimum,
        Padding = new(Tokens.Number("DvSpace1"), 0, Tokens.Number("DvSpace1"), 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    private readonly Border plain = new() { Padding = new(Tokens.Number("DvSpace1"), 0, Tokens.Number("DvSpace1"), 0) };

    private readonly UsagePopover popover = new();

    private ConversationStatusViewModel? model;

    private bool lit;

    #endregion

    #region Constructors

    public ConversationStatus()
    {
        MinHeight = Targets.Minimum;
        Children.Add(plain);
        Children.Add(button);
        AutomationProperties.SetAutomationId(this, "ConversationStatus");
        button.Click += async (_, _) => await popover.Open(button);
        button.PointerEntered += (_, _) => Light(true);
        button.PointerExited += (_, _) => Light(button.FocusState is FocusState.Keyboard);
        button.GotFocus += (_, _) => Light(true);
        button.LostFocus += (_, _) => Light(false);
        SizeChanged += (_, _) => Write();
        this.WhenShownOrRethemed(Show);
    }

    #endregion

    #region Properties

    /// <summary>What the line says: set by the conversation it stands under.</summary>
    public ConversationStatusViewModel? Model
    {
        get => model;
        set
        {
            model?.PropertyChanged -= Changed;
            model = value;
            model?.PropertyChanged += Changed;
            Show();
        }
    }

    #endregion

    #region Private Methods

    private static TextBlock Line()
    {
        var line = Styles.Text(null, "DvTextCaption");
        line.TextWrapping = TextWrapping.NoWrap;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        line.VerticalAlignment = VerticalAlignment.Center;
        return line;
    }

    // A conversation can say what changed from the server's answer, off the screen's own turn.
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            Show();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Show);
        }
    }

    private void Light(bool value)
    {
        lit = value;
        Write();
    }

    // The line is a button only when it opens something. Its words are the same text either way.
    private void Show()
    {
        var opens = model?.Usage is not null;
        popover.Model = model?.Usage;
        Visibility = model?.HasAny is true ? Visibility.Visible : Visibility.Collapsed;
        button.Visibility = opens ? Visibility.Visible : Visibility.Collapsed;
        plain.Visibility = opens ? Visibility.Collapsed : Visibility.Visible;

        if (opens && !ReferenceEquals(button.Content, words))
        {
            plain.Child = null;
            button.Content = words;
        }
        else if (!opens && !ReferenceEquals(plain.Child, words))
        {
            button.Content = null;
            plain.Child = words;
        }

        var name = model?.Name ?? string.Empty;
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetName(words, name);
        ToolTipService.SetToolTip(button, opens ? ConversationStatusViewModel.OpensUsage : null);
        Write();
    }

    // As many of the leading parts as fit the line's width, and never fewer than the first.
    private void Write()
    {
        var parts = model?.Parts ?? [];
        var room = ActualWidth - (2 * Tokens.Number("DvSpace1"));
        var fits = parts.Count;
        Fill(words, parts);

        while (fits > 1 && room > 0 && Measured() > room)
        {
            fits--;
            Fill(words, parts.Take(fits));
        }

        words.Foreground = Tokens.Brush(this, lit ? "DvText2" : "DvText3");

        // What was measured with all the room there is must not stand as the line's size.
        words.InvalidateMeasure();
    }

    private double Measured()
    {
        words.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(words.DesiredSize.Width);
    }

    private void Fill(TextBlock line, IEnumerable<StatusPart> parts)
    {
        line.Inlines.Clear();

        foreach (var part in parts)
        {
            if (line.Inlines.Count > 0)
            {
                line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = ConversationStatusViewModel.Separator });
            }

            var run = new Microsoft.UI.Xaml.Documents.Run { Text = part.Text };
            if (part.IsNumber)
            {
                run.FontFamily = Tokens.FontMono;
            }

            if (part.IsState)
            {
                run.Foreground = Tokens.Brush(this, "DvIon");
            }

            line.Inlines.Add(run);
        }
    }

    #endregion
}
