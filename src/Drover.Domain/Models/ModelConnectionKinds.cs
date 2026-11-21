namespace Drover.Domain.Models;

/// <summary>The names of the kinds of model connection that more than one part of Drover has to recognize.</summary>
public static class ModelConnectionKinds
{
    #region Constants

    /// <summary>
    /// Any server that speaks the common chat protocol. It lists models and nothing more about them, so what a model
    /// can do beyond tools and streaming is what the operator says.
    /// </summary>
    public const string Compatible = "openai_compatible";

    #endregion
}
