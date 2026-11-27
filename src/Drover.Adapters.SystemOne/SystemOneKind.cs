namespace Drover.Adapters.SystemOne;

/// <summary>How a kind sends its key: in which header, and with which word before it, if any.</summary>
public sealed record SystemOneKeyHeader(string Name, string? Prefix);

/// <summary>
/// One provider that serves the state-and-questions API. A kind is its address rule, its auth header, and its
/// envelope, nothing else: the request and the answer are the same wire for every kind. What each kind's provider
/// documents is recorded, with the date it was read, in docs/architecture/15-model-providers.md.
/// </summary>
public sealed record SystemOneKind
{
    #region Properties

    public required string Kind { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The address every connection of this kind uses, or null when a connection gives its own.</summary>
    public Uri? FixedAddress { get; init; }

    /// <summary>Where a request for a model goes, from the connection's address.</summary>
    public required Func<Uri, string, Uri> RequestAddress { get; init; }

    public required SystemOneKeyHeader KeyHeader { get; init; }

    /// <summary>Whether a connection of this kind needs a key. A local server may take none.</summary>
    public bool KeyRequired { get; init; } = true;

    /// <summary>Whether the answer arrives inside a <c>result</c> object, as Cloudflare's REST routes wrap theirs.</summary>
    public bool Enveloped { get; init; }

    /// <summary>The most questions one request carries. More are sent as several requests.</summary>
    public int MaxQuestionsPerRequest { get; init; } = SystemOneKinds.MaxQuestionsPerRequest;

    #endregion
}

/// <summary>The kinds this adapter serves.</summary>
public static class SystemOneKinds
{
    #region Private Fields

    /// <summary>
    /// Cloudflare takes 1 to 64 questions a request, the lowest bound any provider states. The same bound keeps every
    /// other kind's requests to a size a decision model answers quickly.
    /// </summary>
    public const int MaxQuestionsPerRequest = 64;

    private static readonly SystemOneKeyHeader bearer = new("Authorization", "Bearer");

    #endregion

    #region Properties

    public static SystemOneKind TypeSafe { get; } = new()
    {
        Kind = "typesafe",
        DisplayName = "TypeSafe",
        FixedAddress = new("https://api.typesafe.ai/"),
        RequestAddress = (address, _) => Under(address, "v1/systemone"),
        KeyHeader = bearer,
    };

    /// <summary>The address of a connection is the account's: <c>https://api.cloudflare.com/client/v4/accounts/{account}/</c>.</summary>
    public static SystemOneKind CloudflareWorkersAi { get; } = new()
    {
        Kind = "cloudflare_workers_ai",
        DisplayName = "Cloudflare Workers AI",
        RequestAddress = (address, model) => Under(address, $"ai/run/@cf/cloudflare/{Uri.EscapeDataString(model)}"),
        KeyHeader = bearer,
        Enveloped = true,
    };

    public static SystemOneKind OpenRouter { get; } = new()
    {
        Kind = "openrouter",
        DisplayName = "OpenRouter",
        FixedAddress = new("https://openrouter.ai/api/"),
        RequestAddress = (address, _) => Under(address, "alpha/decisions"),
        KeyHeader = bearer,
    };

    /// <summary>The address of a connection is the Foundry resource's endpoint; the model is the deployment's name.</summary>
    public static SystemOneKind MicrosoftFoundry { get; } = new()
    {
        Kind = "microsoft_foundry",
        DisplayName = "Microsoft Foundry",
        RequestAddress = (address, _) => Under(address, "providers/microsoft/v1/systemone"),
        KeyHeader = new("api-key", null),
    };

    /// <summary>Any other server that speaks the API. Its address is the route itself, and a local one may take no key.</summary>
    public static SystemOneKind Compatible { get; } = new()
    {
        Kind = "systemone_compatible",
        DisplayName = "Another server that speaks this API",
        RequestAddress = (address, _) => address,
        KeyHeader = bearer,
        KeyRequired = false,
    };

    public static IReadOnlyList<SystemOneKind> All { get; } = [TypeSafe, CloudflareWorkersAi, OpenRouter, MicrosoftFoundry, Compatible];

    #endregion

    #region Private Methods

    // A route under the address, whether or not the address ends with a slash.
    private static Uri Under(Uri address, string route) =>
        new(address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/"), route);

    #endregion
}
