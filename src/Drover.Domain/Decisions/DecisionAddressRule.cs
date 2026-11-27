namespace Drover.Domain.Decisions;

/// <summary>What a decision model connection gives to say where its provider is.</summary>
public enum DecisionAddressRule
{
    /// <summary>Nothing: every connection of the kind uses one address.</summary>
    Fixed,

    /// <summary>An address a person types, such as a resource's endpoint or a local server.</summary>
    Address,

    /// <summary>An account id; the provider's address follows from it.</summary>
    Account,
}
