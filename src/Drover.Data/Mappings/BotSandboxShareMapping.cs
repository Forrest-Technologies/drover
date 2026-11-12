namespace Drover.Data.Mappings;

public sealed class BotSandboxShareMapping : IEntityTypeConfiguration<BotSandboxShare>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<BotSandboxShare> builder)
    {
        builder.ToTable("bot_sandbox_shares");
        builder.HasKey(row => new { row.SpaceId, row.BotId, row.SharedWithBotId });
        builder.WithinSpace<BotSandboxShare, Bot>(nameof(BotSandboxShare.BotId));
        builder.WithinSpace<BotSandboxShare, Bot>(nameof(BotSandboxShare.SharedWithBotId));
    }

    #endregion
}
