namespace Drover.Domain.Agents;

/// <summary>
/// The project a conversation belongs to, as a run was given it. The instructions are the project's own text.
/// They are a separate source from the bot's instructions, and they grant nothing.
/// </summary>
public sealed record ProjectBrief(Guid Id, string Name, string Instructions);
