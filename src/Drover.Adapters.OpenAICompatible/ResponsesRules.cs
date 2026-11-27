namespace Drover.Adapters.OpenAICompatible;

/// <summary>Which rules a Responses client keeps on top of the wire itself.</summary>
internal enum ResponsesRules
{
    /// <summary>A connection with an API key: the whole wire, whole answers and streams alike.</summary>
    Key,

    /// <summary>
    /// A person's ChatGPT plan, signed in in the browser: every request streams and is shaped by
    /// <see cref="PlanRequestShaping"/>, so only what the plan accepts is sent.
    /// </summary>
    Plan,

    /// <summary>
    /// A person's ChatGPT plan, signed in by code: every request streams and is shaped by
    /// <see cref="PlanRequestShaping.ByCode"/> for the address that flow's tokens are used at.
    /// </summary>
    PlanByCode,
}
