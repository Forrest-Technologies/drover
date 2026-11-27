namespace Drover.Domain.Models;

/// <summary>The reasoning effort levels Drover assumes for a model a server says thinks, when the server names none.</summary>
public static class ReasoningLevels
{
    #region Properties

    /// <summary>
    /// <c>low</c>, <c>medium</c> and <c>high</c>: the levels every provider that takes a reasoning effort at all
    /// accepts, and the set the routing service's <c>reasoning.effort</c> and the local server's <c>think</c> levels
    /// share. UNCONFIRMED against either provider's current documentation, which could not be reached when this was
    /// written; the larger set an admin may choose from (<c>minimal</c> to <c>max</c>) is for models that take more.
    /// </summary>
    public static IReadOnlyList<string> Documented { get; } = ["low", "medium", "high"];

    #endregion
}
