namespace Drover.Domain.Push;

/// <summary>
/// One person's choice that one bot does not notify them. With no row, the bot may. The audience is the person, so
/// the database shows the row to them alone and nobody can mute a bot for someone else.
/// </summary>
public sealed class BotPushMute : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid SpaceId { get; set; }

    /// <summary>The person who muted the bot.</summary>
    public Guid AudienceUserId { get; set; }

    public Guid BotId { get; set; }

    public DateTimeOffset MutedAt { get; set; }

    #endregion
}
