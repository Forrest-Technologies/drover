namespace Drover.Domain.Models;

/// <summary>Whether a connection of a kind supplies its own address.</summary>
public enum ModelAddressRule
{
    /// <summary>Every connection of the kind uses the provider's one address, which the connection never carries.</summary>
    Fixed,

    /// <summary>The connection supplies its own address.</summary>
    Required,
}
