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
            { Status: ModelSignInStatus.NeedsSignIn, AccountLabel: null } => new(ModelSignInStatuses.SignedOut, null),
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
            preset.Local,
            preset.AddressBlank is { } blank ? new ModelProviderAddressBlankResponse(blank.Label, blank.Pattern) : null);

    public static ModelProviderKindResponse ToResponse(this ModelProviderDescriptor descriptor, bool signIn = false, bool personalPlan = false) =>
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
            signIn,
            signIn ? descriptor.SignInName ?? descriptor.DisplayName : null,
            signIn ? descriptor.UsagePage?.AbsoluteUri : null,
            signIn && personalPlan,
            signIn ? descriptor.SignInReturn?.AbsoluteUri : null);

    public static ModelCatalogResponse ToResponse(this ModelCatalogEntry entry)
    {
        var capabilities = entry.EffectiveCapabilities;
        var pricing = entry.EffectivePricing;

        return new ModelCatalogResponse(
            entry.Model,
            entry.DisplayName,
            new ModelCapabilitiesResponse(
                capabilities.ContextTokens,
                capabilities.MaxOutputTokens,
                capabilities.ToolCalling,
                capabilities.Streaming,
                capabilities.Vision,
                capabilities.Reasoning,
                [.. capabilities.ReasoningEfforts],
                capabilities.StructuredOutput,
                capabilities.WebSearch,
                capabilities.ImageGeneration,
                entry.OperatorCapabilities?.Vision is true,
                capabilities.SpeechToText,
                capabilities.TextToSpeech,
                capabilities.RealtimeVoice,
                capabilities.VoiceSetByOperator,
                capabilities.PdfInput),
            new ModelPriceResponse(
                pricing.InputPerMillion,
                pricing.OutputPerMillion,
                pricing.CachedInputPerMillion,
                pricing.Currency),
            entry.PrivacyMode,
            [.. entry.Traits],
            entry.State.ToString().ToLowerInvariant(),
            entry.LastSeenAt,
            ListOperatorSet(entry));
    }

    /// <summary>What an admin typed, as the stored overrides. No request at all sets nothing.</summary>
    public static ModelCapabilityOverrides ToOverrides(this ModelAbilitiesRequest? request) =>
        request is null
            ? new ModelCapabilityOverrides()
            : new ModelCapabilityOverrides
            {
                ContextTokens = request.ContextSize,
                MaxOutputTokens = request.MaxOutputSize,
                ToolCalling = request.ToolCalling,
                Vision = request.Vision,
                Reasoning = request.Reasoning,
                ReasoningEfforts = request.ReasoningEfforts,
                StructuredOutput = request.StructuredOutput,
                ImageGeneration = request.ImageGeneration,
            };

    #endregion

    #region Private Methods

    /// <summary>The members an admin set, by the names the response's own members carry in camel case.</summary>
    private static List<string> ListOperatorSet(ModelCatalogEntry entry)
    {
        List<string> names = [];
        var abilities = entry.OperatorCapabilities;
        var prices = entry.OperatorPricing;
        AddWhen(names, abilities?.ContextTokens is not null, "contextSize");
        AddWhen(names, abilities?.MaxOutputTokens is not null, "maxOutputSize");
        AddWhen(names, abilities?.ToolCalling is not null, "toolCalling");
        AddWhen(names, abilities?.Vision is not null, "vision");
        AddWhen(names, abilities?.Reasoning is not null, "reasoning");
        AddWhen(names, abilities?.ReasoningEfforts is not null, "reasoningEfforts");
        AddWhen(names, abilities?.StructuredOutput is not null, "structuredOutput");
        AddWhen(names, abilities?.ImageGeneration is not null, "imageGeneration");
        AddWhen(names, prices?.InputPerMillion is not null, "inputPerMillion");
        AddWhen(names, prices?.OutputPerMillion is not null, "outputPerMillion");
        AddWhen(names, prices?.CachedInputPerMillion is not null, "cachedInputPerMillion");
        return names;
    }

    private static void AddWhen(List<string> names, bool condition, string name)
    {
        if (condition)
        {
            names.Add(name);
        }
    }

    #endregion
}


/// <summary>A connection each person signs in to, with the caller's own sign-in, or null when they have none.</summary>
public sealed record SignInView(ModelConnectionCredential? Credential);
