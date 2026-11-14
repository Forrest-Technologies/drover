namespace Drover.Adapters.E2B;

/// <summary>The operator's E2B account. The key is configuration held in memory, never a vault secret or a response.</summary>
public sealed class E2BOptions
{
    /// <summary>The team API key. E2B keys start with <c>e2b_</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The control plane is <c>api.{Domain}</c> and each sandbox's daemon is <c>49983-{id}.{Domain}</c>.</summary>
    public string Domain { get; set; } = "e2b.app";

    /// <summary>The template a sandbox starts from when the spec names none.</summary>
    public string Template { get; set; } = "base";
}
