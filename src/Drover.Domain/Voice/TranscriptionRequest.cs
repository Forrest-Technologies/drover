namespace Drover.Domain.Voice;

/// <summary>One recording to write down. <see cref="MaxBytes"/> bounds what is read from <see cref="Audio"/>.</summary>
public sealed record TranscriptionRequest(Stream Audio, string MediaType, string Model, long MaxBytes)
{
    #region Properties

    /// <summary>The spoken language as a two-letter code when the person chose one. Null lets the model detect it.</summary>
    public string? Language { get; init; }

    #endregion
}


/// <summary>The words of a recording. <see cref="Seconds"/> is the provider's own measure, when it gave one.</summary>
public sealed record Transcript(string Text)
{
    #region Properties

    public double? Seconds { get; init; }

    public string? Language { get; init; }

    #endregion
}
