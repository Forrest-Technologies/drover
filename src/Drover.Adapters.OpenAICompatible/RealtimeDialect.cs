namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The realtime protocol has one set of events with two differences between servers: whether the voice and the
/// turn detection sit on the session itself or inside its audio settings, and whether the server can tell the end
/// of a turn from what was said and clean the microphone's sound before it listens. Only the service the protocol
/// comes from is known to do the second; every other server is asked for turns by silence, which they all take.
/// Chosen by the server's host.
/// </summary>
internal sealed record RealtimeDialect(bool FlatSession, bool ListensForMeaning)
{
    #region Private Fields

    private static readonly RealtimeDialect standard = new(false, false);

    private static readonly RealtimeDialect hosted = new(false, true);

    private static readonly RealtimeDialect grok = new(true, false);

    #endregion

    #region Public Methods

    public static RealtimeDialect For(Uri address) =>
        KnownVoices.IsGrok(address) ? grok : KnownVoices.IsHosted(address) ? hosted : standard;

    #endregion
}
