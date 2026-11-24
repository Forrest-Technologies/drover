namespace Drover.Native.Chat;

/// <summary>
/// The one quiet line under a conversation's composer: what the bot is doing now, in words, then the model that
/// wrote the newest finished reply and what that reply used, as one button that opens the whole of it
/// (<see cref="UsageHint"/>). Each part is there only when it has something to say, and with nothing to say the line
/// takes no room. It stays one line on a narrow screen: the model's name is cut short first.
/// </summary>
public sealed class ConversationStatus : Grid
{
    #region Private Fields

    private readonly TextBlock state = Styles.Text(null, "DvTextCaption");

    private readonly UsageHint usage = new() { VerticalAlignment = VerticalAlignment.Center };

    private ConversationStatusViewModel? model;

    #endregion

    #region Constructors

    public ConversationStatus()
    {
        ColumnSpacing = Tokens.Number("DvSpace2");
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        state.VerticalAlignment = VerticalAlignment.Center;
        state.TextWrapping = TextWrapping.NoWrap;
        state.Margin = new(Tokens.Number("DvSpace1"), 0, 0, 0);
        SetColumn(usage, 1);
        Children.Add(state);
        Children.Add(usage);
        AutomationProperties.SetName(this, Name);
        AutomationProperties.SetAutomationId(this, "ConversationStatus");
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

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Group);

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

    private void Show()
    {
        var said = model?.State;
        state.Text = said ?? string.Empty;
        state.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;
        state.Foreground = Tokens.Brush(this, "DvIon");
        usage.Model = model?.Usage;
        Visibility = model?.HasAny is true ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Fixed Words

    public new const string Name = "Conversation status";

    #endregion
}
