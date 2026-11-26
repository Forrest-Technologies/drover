namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The realtime protocol has one set of events with one difference between servers: whether the voice and the
/// turn detection sit on the session itself or inside its audio settings. Chosen by the server's host.
/// </summary>
internal sealed record RealtimeDialect(bool FlatSession)
{
    #region Private Fields

    private static readonly RealtimeDialect standard = new(false);

    private static readonly RealtimeDialect grok = new(true);

    #endregion

    #region Public Methods

    public static RealtimeDialect For(Uri address) => KnownVoices.IsGrok(address) ? grok : standard;

    #endregion
}
