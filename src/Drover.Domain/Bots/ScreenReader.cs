namespace Drover.Domain.Bots;

/// <summary>
/// A model of the bot's space that reads the sandbox's screen for the bot: each screenshot goes to it, and the bot gets
/// its description in words instead of the picture. Set when the bot's own model cannot read images, or to override it.
/// </summary>
public sealed record ScreenReader(Guid ModelConnectionId, string Model);
