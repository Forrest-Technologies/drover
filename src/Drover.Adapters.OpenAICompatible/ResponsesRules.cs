namespace Drover.Adapters.OpenAICompatible;

/// <summary>Which rules a Responses client keeps on top of the wire itself.</summary>
internal enum ResponsesRules
{
    /// <summary>A connection with an API key: the whole wire, whole answers and streams alike.</summary>
    Key,

    /// <summary>
    /// A person's ChatGPT plan: every request streams and is shaped by <see cref="PlanRequestShaping"/>, so only what
    /// the plan accepts is sent.
    /// </summary>
    Plan,
}
