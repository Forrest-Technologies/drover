namespace Drover.Domain.Voice;

/// <summary>How a live call starts: which model and voice, what the bot is told, and what was said before.</summary>
public sealed record RealtimeVoiceOptions(string Model, string Voice, string Instructions)
{
    #region Properties

    /// <summary>The earlier conversation, oldest first, so the call carries on from it.</summary>
    public IReadOnlyList<RealtimeTurn> History { get; init; } = [];

    /// <summary>The model that writes down what the person says, for the transcript. Null uses the provider's default.</summary>
    public string? TranscriptionModel { get; init; }

    #endregion
}


/// <summary>One earlier message given to a live call as context.</summary>
public sealed record RealtimeTurn(bool FromBot, string Text);
