namespace Drover.Domain.Voice;

/// <summary>
/// What a space chose for speech. A slot left null is chosen by the server: the first working connection with a
/// model marked for it, or the server's own speech service when the space has none.
/// </summary>
public sealed record VoiceSettings
{
    #region Properties

    /// <summary>The model that writes down what a person says.</summary>
    public VoiceChoice? Listen { get; init; }

    /// <summary>The model that reads a reply aloud.</summary>
    public VoiceChoice? Speak { get; init; }

    /// <summary>The model that holds a live call.</summary>
    public VoiceChoice? Live { get; init; }

    #endregion
}


/// <summary>One model of one connection, and the voice it speaks in. A null voice is the model's first.</summary>
public sealed record VoiceChoice(Guid ConnectionId, string Model, string? Voice);
