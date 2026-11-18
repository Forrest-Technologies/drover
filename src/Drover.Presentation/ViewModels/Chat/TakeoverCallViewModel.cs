namespace Drover.Presentation.ViewModels.Chat;

/// <summary>A bot's request for a person at its screen, in the conversation: the card, with what is on its way to the server.</summary>
public sealed class TakeoverCallViewModel : ObservableObject
{
    #region Properties

    public Takeover Takeover { get; set; } = null!;

    public string BotName { get; set; } = string.Empty;

    /// <summary>The page around the conversation: the held request, the person's access, and the steps sent.</summary>
    public ScreenTakeovers Takeovers { get; set; } = null!;

    /// <summary>True while a step is on its way to the server.</summary>
    public bool Sending { get; private set; }

    /// <summary>Why the step could not be sent, as a sentence.</summary>
    public string? Problem { get; private set; }

    /// <summary>The person took the screen over for this request and has it open, so they answer there.</summary>
    public bool Held => Takeovers.HeldCallId == Takeover.ToolCallId;

    public TakeoverPhase Phase => Takeover.Phase is TakeoverPhase.Asked && Held ? TakeoverPhase.Holding : Takeover.Phase;

    #endregion

    #region Public Methods

    public Task Open() => Send(() => Takeovers.OpenScreen?.Invoke(Takeover) ?? Task.FromResult<string?>(null));

    public Task Answer(bool done, string? note) =>
        Send(() => Takeovers.HandBack?.Invoke(Takeover.ToolCallId, done, note) ?? Task.FromResult<string?>(null));

    #endregion

    #region Private Methods

    private async Task Send(Func<Task<string?>> request)
    {
        if (Sending)
        {
            return;
        }

        (Sending, Problem) = (true, null);
        OnPropertyChanged(string.Empty);
        Problem = await request();
        Sending = false;
        OnPropertyChanged(string.Empty);
    }

    #endregion
}
