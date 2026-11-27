namespace Drover.Domain.Models;

/// <summary>
/// What a model can do. A generic endpoint defaults to tools and streaming and nothing else, so a bot is never
/// offered a feature the model lacks.
/// </summary>
public sealed record ModelCapabilities
{
    #region Properties

    public int? ContextTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool ToolCalling { get; init; } = true;

    public bool Streaming { get; init; } = true;

    /// <summary>Whether the model reads pictures. For a model whose provider does not say, see <see cref="VisionSetByOperator"/>.</summary>
    public bool Vision { get; init; }

    /// <summary>
    /// True when a space admin marked the model as reading pictures. A provider that does not know its models'
    /// capabilities reports none, so a refresh keeps this mark and <see cref="Vision"/> with it.
    /// </summary>
    public bool VisionSetByOperator { get; init; }

    /// <summary>
    /// Whether the model reads a PDF as a PDF, its pages and pictures included. A model without it is given the text
    /// the server reads out of the file. Only a provider that says so per model sets it.
    /// </summary>
    public bool PdfInput { get; init; }

    /// <summary>The most pages of one PDF the provider takes for this model, or null when it sets no bound.</summary>
    public int? PdfMaxPages { get; init; }

    /// <summary>The most bytes of one PDF the provider takes for this model, or null when it sets no bound.</summary>
    public long? PdfMaxBytes { get; init; }

    public bool Reasoning { get; init; }

    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];

    public bool StructuredOutput { get; init; }

    public bool WebSearch { get; init; }

    public bool ImageGeneration { get; init; }

    /// <summary>Whether the model writes down speech. See <see cref="VoiceSetByOperator"/>.</summary>
    public bool SpeechToText { get; init; }

    /// <summary>Whether the model reads text aloud. See <see cref="VoiceSetByOperator"/>.</summary>
    public bool TextToSpeech { get; init; }

    /// <summary>Whether the model holds a live spoken conversation. See <see cref="VoiceSetByOperator"/>.</summary>
    public bool RealtimeVoice { get; init; }

    /// <summary>
    /// True when a space admin set the three voice marks by hand. A refresh then keeps them, as it keeps
    /// <see cref="VisionSetByOperator"/>, because a provider that lists only names cannot say otherwise.
    /// </summary>
    public bool VoiceSetByOperator { get; init; }

    #endregion
}
