namespace Drover.Domain.Bots;

/// <summary>How a bot is pictured. With neither field set the interface shows initials and the bot's own hue.</summary>
public sealed record BotAvatar
{
    public string? Emoji { get; init; }

    public Guid? FileId { get; init; }
}
