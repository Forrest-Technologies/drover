namespace Drover.Domain.Models;

/// <summary>The reasoning effort levels Drover assumes for a model a server says thinks, when the server names none.</summary>
public static class ReasoningLevels
{
    #region Properties

    /// <summary>
    /// <c>low</c>, <c>medium</c> and <c>high</c>: the levels the routing service and the local server both accept for a
    /// reasoning effort. The routing service maps a level a model does not support to the nearest one it does, and the
    /// local server treats a recognised level as "on" or falls back to the model's default, so neither errors on one a
    /// model lacks. Sources: https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion,
    /// https://openrouter.ai/docs/guides/best-practices/reasoning-tokens and
    /// https://docs.ollama.com/api/openai-compatibility. The exact levels of each model are not read yet; the larger set
    /// an admin may choose from (<c>minimal</c> to <c>max</c>) is for models that take more.
    /// </summary>
    public static IReadOnlyList<string> Documented { get; } = ["low", "medium", "high"];

    #endregion
}
