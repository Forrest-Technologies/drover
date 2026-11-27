namespace Drover.Domain.Models;

/// <summary>Whether a connection of a kind needs a key.</summary>
public enum ModelKeyRule
{
    /// <summary>The provider refuses every call without a key.</summary>
    Required,

    /// <summary>The key is optional: a local server needs none, a hosted one does.</summary>
    Optional,

    /// <summary>The provider takes no key.</summary>
    None,
}
