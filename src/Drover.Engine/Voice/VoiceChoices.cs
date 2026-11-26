namespace Drover.Engine.Voice;

/// <summary>The three things speech does. Each is chosen on its own.</summary>
public enum VoiceSlot
{
    /// <summary>Writing down what a person says.</summary>
    Listen,

    /// <summary>Reading a reply aloud.</summary>
    Speak,

    /// <summary>Holding a live call.</summary>
    Live,
}


/// <summary>
/// The server's own speech service, from the server's configuration. An address left null means the server has
/// none for that slot. Only the operator sets these; no space can change them.
/// </summary>
public sealed record ServerVoiceOptions
{
    #region Properties

    public Uri? SpeechAddress { get; init; }

    public string SpeechModel { get; init; } = DefaultSpeechModel;

    public string SpeechVoice { get; init; } = DefaultSpeechVoice;

    public Uri? TranscriptionAddress { get; init; }

    public string? TranscriptionModel { get; init; }

    #endregion

    #region Constants

    public const string DefaultSpeechModel = "kokoro";

    public const string DefaultSpeechVoice = "af_heart";

    #endregion
}


/// <summary>A connection of the space with its catalog, as the choice is made from them.</summary>
public sealed record VoiceSource(ModelConnection Connection, IReadOnlyList<ModelCatalogEntry> Catalog);

/// <summary>
/// One model that can fill a slot. <see cref="ConnectionId"/> is null for the server's own speech service, which
/// has no connection in any space.
/// </summary>
public sealed record VoiceCandidate(Guid? ConnectionId, string ConnectionName, string Kind, Uri? Address, string Model)
{
    public bool IsServer => ConnectionId is null;
}

/// <summary>The model a slot uses now, the voice chosen for it if any, and whether the server chose it.</summary>
public sealed record VoicePick(VoiceCandidate Candidate, string? Voice, bool Automatic);

/// <summary>
/// Decides which model fills each slot. The space's own choice is used while its connection is still there and its
/// model is still marked for the slot. Otherwise the server chooses: the first connection by name that has not
/// failed and has such a model, the first of those by model name, and last the server's own speech service.
/// </summary>
public static class VoiceChoices
{
    #region Private Fields

    private static readonly string[] localSuffixes = [".local", ".internal", ".lan", ".home.arpa", ".localhost"];

    public const string ServerName = "This server";

    #endregion

    #region Public Methods

    public static bool Fills(this ModelCapabilities capabilities, VoiceSlot slot) =>
        slot switch
        {
            VoiceSlot.Listen => capabilities.SpeechToText,
            VoiceSlot.Speak => capabilities.TextToSpeech,
            _ => capabilities.RealtimeVoice,
        };

    public static VoiceChoice? For(this VoiceSettings? settings, VoiceSlot slot) =>
        slot switch
        {
            VoiceSlot.Listen => settings?.Listen,
            VoiceSlot.Speak => settings?.Speak,
            _ => settings?.Live,
        };

    /// <summary>Every model that can fill <paramref name="slot"/>, in the order the automatic choice prefers them.</summary>
    public static IReadOnlyList<VoiceCandidate> Candidates(VoiceSlot slot, IReadOnlyList<VoiceSource> sources, ServerVoiceOptions server) =>
    [
        .. sources
            .OrderBy(source => source.Connection.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Connection.Id)
            .SelectMany(source => source.Catalog
                .Where(entry => entry.State == AvailabilityState.Available && entry.Capabilities.Fills(slot))
                .OrderBy(entry => entry.Model, StringComparer.Ordinal)
                .Select(entry => new VoiceCandidate(
                    source.Connection.Id,
                    source.Connection.Name,
                    source.Connection.Kind,
                    Uri.TryCreate(source.Connection.BaseUrl, UriKind.Absolute, out var address) ? address : null,
                    entry.Model))),
        .. ServerCandidates(slot, server),
    ];

    public static VoicePick? Resolve(VoiceSlot slot, VoiceSettings? settings, IReadOnlyList<VoiceSource> sources, ServerVoiceOptions server)
    {
        var candidates = Candidates(slot, sources, server);

        if (settings.For(slot) is { } chosen
            && candidates.FirstOrDefault(candidate => candidate.ConnectionId == chosen.ConnectionId && candidate.Model == chosen.Model) is { } kept)
        {
            return new VoicePick(kept, string.IsNullOrWhiteSpace(chosen.Voice) ? null : chosen.Voice, Automatic: false);
        }

        var working = sources
            .Where(source => source.Connection.Status is not (ConnectionStatus.Failed or ConnectionStatus.Disabled))
            .Select(source => source.Connection.Id)
            .ToHashSet();
        var automatic = candidates.FirstOrDefault(candidate => candidate.ConnectionId is { } id && working.Contains(id))
            ?? candidates.FirstOrDefault(candidate => candidate.IsServer);

        return automatic is null
            ? null
            : new VoicePick(automatic, automatic.IsServer && slot is VoiceSlot.Speak ? server.SpeechVoice : null, Automatic: true);
    }

    /// <summary>
    /// Whether an address is on a private network as far as its text says: a private or loopback number, a name
    /// with no dot (a service beside the server), or a name that ends in a local suffix. A hint for the screen that
    /// says where speech goes, never a security decision: those are made when a connection is opened.
    /// </summary>
    public static bool LooksPrivate(Uri? address)
    {
        if (address is null)
        {
            return false;
        }

        var host = address.IdnHost;

        if (IPAddress.TryParse(host, out var literal))
        {
            return PrivateAddresses.Contains(literal) || IPAddress.IsLoopback(literal);
        }

        return !host.Contains('.', StringComparison.Ordinal)
            || localSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Private Methods

    private static IEnumerable<VoiceCandidate> ServerCandidates(VoiceSlot slot, ServerVoiceOptions server) =>
        slot switch
        {
            VoiceSlot.Speak when server.SpeechAddress is { } address =>
                [new VoiceCandidate(null, ServerName, ModelConnectionKinds.ServerSpeech, address, server.SpeechModel)],
            VoiceSlot.Listen when server is { TranscriptionAddress: { } address, TranscriptionModel: { Length: > 0 } model } =>
                [new VoiceCandidate(null, ServerName, ModelConnectionKinds.ServerSpeech, address, model)],
            _ => [],
        };

    #endregion
}
