namespace Drover.Adapters.Anthropic;

/// <summary>
/// Anthropic with a person's own Claude plan, on the same Messages client and fixed address as the key kind. The
/// person's access token is the bearer credential and no <c>x-api-key</c> is sent; every call and model list carries
/// the configured headers, and every call sends the configured prefix as its first system block with the bot's
/// instructions as the second. Settings are read for each client, so a change of them reaches the next call without a
/// restart. The token comes from the caller, renewed by the engine, and is never stored here.
/// </summary>
public sealed class AnthropicPlanProvider(IHttpClientFactory httpClients, IOptionsMonitor<AnthropicPlanSignInOptions> options) : IModelClientProvider
{
    #region Private Fields

    private const string SignInMissing = "Sign in with your Claude plan to finish setup.";

    #endregion

    #region Properties

    public string Kind => AnthropicPlanSignIn.ConnectionKind;

    public Uri? FixedAddress => AnthropicProvider.BaseAddress;

    /// <summary>
    /// Every call needs a credential, the person's token, so the rule is the key kind's; a person never types it, and a
    /// connection of this kind is never made with a key or an address.
    /// </summary>
    public ModelProviderDescriptor Descriptor { get; } = new(
        AnthropicPlanSignIn.ConnectionKind,
        "Claude plan",
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: null,
        ReportsCapabilities: false,
        SignInName: "Claude");

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return new AnthropicChatClient(httpClients, AnthropicProvider.HttpClientName, Destination(apiKey), connection.Limits, modelId, settings);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        using var client = httpClients.CreateClient(AnthropicProvider.HttpClientName);
        return await AnthropicModels.List(client, Destination(apiKey), connection.Limits, cancellation);
    }

    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, SignInMissing);
        }

        try
        {
            using var client = httpClients.CreateClient(AnthropicProvider.HttpClientName);
            await AnthropicModels.CheckKey(client, Destination(apiKey), connection.Limits, cancellation);
            return ConnectionTestResult.Ok("Connected to your Claude plan.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>The fixed address, the person's token, and the settings as they are now, copied so a change mid-call cannot reach it.</summary>
    private AnthropicDestination Destination(string token)
    {
        var settings = options.CurrentValue;
        return new AnthropicDestination(
            AnthropicProvider.BaseAddress,
            token,
            new Dictionary<string, string>(settings.Headers, StringComparer.OrdinalIgnoreCase),
            settings.SystemPrefix);
    }

    #endregion
}
