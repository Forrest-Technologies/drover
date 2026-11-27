namespace Drover.Server.Models;

/// <summary>Maps Drover's model records to what the API returns. Nothing here can reach a key.</summary>
public static class ModelResponses
{
    #region Public Methods

    /// <summary>
    /// The connection as the caller sees it. <paramref name="signIn"/> says whether the connection is one each person
    /// signs in to, and if so the caller's own sign-in, or null for none. It is never another person's.
    /// </summary>
    public static ModelConnectionSummary ToSummary(this ModelConnection connection, SignInView? signIn = null) =>
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
            connection.LastCheckedAt,
            signIn is null ? null : ToSignInSummary(signIn.Credential));

    public static ModelSignInSummary ToSignInSummary(ModelConnectionCredential? credential) =>
        credential switch
        {
            null => new(ModelSignInStatuses.None, null),
            { Status: ModelSignInStatus.NeedsSignIn } => new(ModelSignInStatuses.NeedsSignIn, credential.AccountLabel),
            _ => new(ModelSignInStatuses.Active, credential.AccountLabel),
        };

    public static ModelProviderPresetResponse ToResponse(this ModelProviderPreset preset) =>
        new(
            preset.Id,
            preset.DisplayName,
            preset.Address,
            preset.KeyRule.ToString().ToLowerInvariant(),
            preset.KeyPage,
            preset.Local);

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
            descriptor.ReportsCapabilities,
            [.. (descriptor.NamedProviders ?? []).Select(named => named.ToResponse())],
            descriptor.SignIn);

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


/// <summary>A connection each person signs in to, with the caller's own sign-in, or null when they have none.</summary>
public sealed record SignInView(ModelConnectionCredential? Credential);
