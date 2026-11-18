namespace Drover.Native.Chat;

/// <summary>A bot's request for a person at its screen, in the conversation: the card, with what is on its way to the server.</summary>
public sealed class TakeoverCall : Grid
{
    #region Private Fields

    private readonly TakeoverCallViewModel model = new();

    private readonly Batch batch;

    private readonly TakeoverCard card = new();

    #endregion

    #region Constructors

    public TakeoverCall()
    {
        batch = new(this, Apply, Draw);
        card.OpenScreen += async (_, _) => await model.Open();
        card.Done += async (_, _) => await model.Answer(true, null);
        card.CouldNot += async (_, note) => await model.Answer(false, note);
        Children.Add(card);
        model.PropertyChanged += (_, _) => batch.Draw();
        batch.Draw();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TakeoverProperty = Dp.Register<TakeoverCall, Takeover?>(nameof(Takeover), null, call => call.batch.Values());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<TakeoverCall, string>(nameof(BotName), string.Empty, call => call.batch.Values());

    public static readonly DependencyProperty TakeoversProperty =
        Dp.Register<TakeoverCall, ScreenTakeovers?>(nameof(Takeovers), null, call => call.batch.Values());

    /// <summary>The bot's request. Required.</summary>
    public Takeover? Takeover
    {
        get => (Takeover?)GetValue(TakeoverProperty);
        set => SetValue(TakeoverProperty, value);
    }

    /// <summary>The bot that asks. Required.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>The page around the conversation: the held request, the person's access, and the steps sent. Required.</summary>
    public ScreenTakeovers? Takeovers
    {
        get => (ScreenTakeovers?)GetValue(TakeoversProperty);
        set => SetValue(TakeoversProperty, value);
    }

    #endregion

    #region Private Methods

    private void Apply()
    {
        if (Takeover is { } takeover && Takeovers is { } takeovers)
        {
            (model.Takeover, model.BotName, model.Takeovers) = (takeover, BotName, takeovers);
        }
    }

    private void Draw()
    {
        if (Takeover is null || Takeovers is null || model.Takeover is null)
        {
            card.Visibility = Visibility.Collapsed;
            return;
        }

        card.Visibility = Visibility.Visible;
        (card.BotName, card.Reason, card.Phase) = (model.BotName, model.Takeover.Reason, model.Phase);
        (card.Access, card.SetupHref, card.AnswerBesideScreen) = (model.Takeovers.Access, model.Takeovers.SetupHref, model.Held);
        (card.Sending, card.Problem) = (model.Sending, model.Problem);
    }

    #endregion
}
