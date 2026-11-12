namespace Drover.Domain.Models;

/// <summary>Keys an adapter sets in the additional properties of a chat response or content part.</summary>
public static class ModelResponseKeys
{
    /// <summary>On a reasoning part whose text the provider withheld. The value is <c>true</c>.</summary>
    public const string ReasoningNotAvailable = "drover.reasoning_not_available";

    /// <summary>On a response or update: the balances a provider reported, as a list of <see cref="ModelBalance"/>.</summary>
    public const string Balances = "drover.balances";
}
