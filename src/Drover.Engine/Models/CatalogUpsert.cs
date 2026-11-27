namespace Drover.Engine.Models;

/// <summary>
/// Folds a provider's model list into the catalog rows a connection already has. A model the provider no longer
/// lists keeps its row, marked missing, so a bot that uses it can say why it stopped working.
/// </summary>
public static class CatalogUpsert
{
    #region Private Fields

    public const int MaxModelIdLength = 200;

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates <paramref name="existing"/> in place and returns the rows to add. An empty list from the provider
    /// marks nothing missing: a server that does not list its models looks the same as one that lists none.
    /// </summary>
    public static IReadOnlyList<ModelCatalogEntry> Apply(
        Guid connectionId,
        List<ModelCatalogEntry> existing,
        IReadOnlyList<ModelInfo> discovered,
        DateTimeOffset now)
    {
        var listed = discovered
            .Where(model => model.Id.Length is > 0 and <= MaxModelIdLength)
            .DistinctBy(model => model.Id, StringComparer.Ordinal)
            .ToList();
        var byId = existing.ToDictionary(entry => entry.Model, StringComparer.Ordinal);
        List<ModelCatalogEntry> added = [];

        foreach (var model in listed)
        {
            if (byId.TryGetValue(model.Id, out var entry))
            {
                Refresh(entry, model, now);
            }
            else
            {
                var created = new ModelCatalogEntry { ModelConnectionId = connectionId, Model = model.Id, DisplayName = model.DisplayName };
                Refresh(created, model, now);
                added.Add(created);
            }
        }

        if (listed.Count > 0)
        {
            var seen = listed.Select(model => model.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var entry in existing.Where(entry => !seen.Contains(entry.Model)))
            {
                entry.State = AvailabilityState.Missing;
            }
        }

        return added;
    }

    #endregion

    #region Private Methods

    private static void Refresh(ModelCatalogEntry entry, ModelInfo model, DateTimeOffset now)
    {
        entry.DisplayName = Truncate(model.DisplayName, MaxModelIdLength);
        entry.Capabilities = model.FactsRead ? KeepOperatorMarks(entry.Capabilities, model.Capabilities) : entry.Capabilities;
        entry.Pricing = model.FactsRead ? model.Pricing : entry.Pricing;
        entry.PrivacyMode = model.Privacy is PrivacyMode.Unknown ? null : model.Privacy.ToString().ToLowerInvariant();
        entry.Traits = [.. model.Traits];
        entry.State = AvailabilityState.Available;
        entry.LastSeenAt = now;
    }

    /// <summary>
    /// What an admin marked for speech outlives a refresh: the provider's list cannot contradict it. What an admin
    /// set for the other abilities and for prices lives in its own columns, which a refresh never writes.
    /// </summary>
    private static ModelCapabilities KeepOperatorMarks(ModelCapabilities kept, ModelCapabilities listed) =>
        kept.VoiceSetByOperator
            ? listed with
            {
                SpeechToText = kept.SpeechToText,
                TextToSpeech = kept.TextToSpeech,
                RealtimeVoice = kept.RealtimeVoice,
                VoiceSetByOperator = true,
            }
            : listed;

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    #endregion
}
