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
                entry.Capabilities.WebSearch),
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
