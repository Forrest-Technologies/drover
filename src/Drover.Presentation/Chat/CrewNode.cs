namespace Drover.Ui.Chat;

/// <summary>One bot in the crew and the bots beneath it.</summary>
public sealed record CrewNode(BotSummary Bot, IReadOnlyList<CrewNode> Children);
