namespace Drover.Domain.Bots;

/// <summary>For the <c>named</c> sandbox policy: another bot that may use this bot's sandbox.</summary>
public sealed class BotSandboxShare : ISpaceOwned
{
    public Guid SpaceId { get; set; }

    public Guid BotId { get; set; }

    public Guid SharedWithBotId { get; set; }
}
