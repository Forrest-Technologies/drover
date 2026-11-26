namespace Drover.Server.Models;

/// <summary>Maps Drover's model records to what the API returns. Nothing here can reach a key.</summary>
public static class ModelResponses
{
    #region Public Methods

    public static ModelConnectionSummary ToSummary(this ModelConnection connection) =>
        new(
            connection.Id,
            connection.Name,
            connection.Kind,
            connection.BaseUrl,
            HasKey: connection.SecretId is not null,
            connection.Status.ToString().ToLowerInvariant(),
            connection.DefaultModel,
            [.. (connection.Balance?.Amounts ?? []).Select(balance => new BalanceAmount(balance.Currency, balance.Amount))],
            connection.Balance?.ReadAt,
            connection.LastCheckedAt);

    public static ModelProviderKindResponse ToResponse(this ModelProviderDescriptor descriptor) =>
        new(
            descriptor.Kind,
            descriptor.DisplayName,
            descriptor.KeyRule switch
            {
                ModelKeyRule.Required => ModelKeyRules.Required,
                ModelKeyRule.Optional => ModelKeyRules.Optional,
                _ => ModelKeyRules.None,
            },
            descriptor.AddressRule is ModelAddressRule.Fixed ? ModelAddressRules.Fixed : ModelAddressRules.Required,
            descriptor.AddressHint,
            descriptor.KeyHint,
            descriptor.ReportsCapabilities);

    public static ModelCatalogResponse ToResponse(this ModelCatalogEntry entry) =>
        new(
            entry.Model,
            entry.DisplayName,
            new ModelCapabilitiesResponse(
                entry.Capabilities.ContextTokens,
                entry.Capabilities.MaxOutputTokens,
                entry.Capabilities.ToolCalling,
                entry.Capabilities.Streaming,
                entry.Capabilities.Vision,
                entry.Capabilities.Reasoning,
                [.. entry.Capabilities.ReasoningEfforts],
                entry.Capabilities.StructuredOutput,
                entry.Capabilities.WebSearch,
                entry.Capabilities.ImageGeneration,
                entry.Capabilities.VisionSetByOperator,
                entry.Capabilities.SpeechToText,
                entry.Capabilities.TextToSpeech,
                entry.Capabilities.RealtimeVoice,
                entry.Capabilities.VoiceSetByOperator),
            new ModelPriceResponse(
                entry.Pricing.InputPerMillion,
                entry.Pricing.OutputPerMillion,
                entry.Pricing.CachedInputPerMillion,
                entry.Pricing.Currency),
            entry.PrivacyMode,
            [.. entry.Traits],
            entry.State.ToString().ToLowerInvariant(),
            entry.LastSeenAt);

    #endregion
}
