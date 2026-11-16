namespace Drover.Data.Mappings;

public sealed class BotPushMuteMapping : IEntityTypeConfiguration<BotPushMute>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<BotPushMute> builder)
    {
        builder.ToTable("bot_push_mutes");
        builder.HasKey(mute => new { mute.SpaceId, mute.BotId, mute.AudienceUserId });
        builder.WithinSpace<BotPushMute, Bot>(nameof(BotPushMute.BotId));
    }

    #endregion
}
