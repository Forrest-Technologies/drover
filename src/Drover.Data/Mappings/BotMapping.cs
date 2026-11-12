namespace Drover.Data.Mappings;

public sealed class BotMapping : IEntityTypeConfiguration<Bot>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Bot> builder)
    {
        builder.ToTable("bots");
        builder.HasKey(bot => bot.Id);
        builder.Property(bot => bot.Id).ValueGeneratedNever();
        builder.Property(bot => bot.Name).HasMaxLength(200);
        builder.Property(bot => bot.Handle).HasMaxLength(100);
        builder.Property(bot => bot.Model).HasMaxLength(200);
        builder.Property(bot => bot.Avatar).HasJsonRecord();
        builder.Property(bot => bot.ModelSettings).HasJsonRecord();
        builder.Property(bot => bot.MemorySettings).HasJsonRecord();
        builder.Property(bot => bot.SandboxSettings).HasJsonRecord();
        builder.Property(bot => bot.BuildLimits).HasJsonRecord();
        builder.HasEnumColumn(bot => bot.SandboxPolicy);
        builder.HasEnumColumn(bot => bot.Status);
        builder.ToTable(table => table.HasCheckConstraint("ck_bots_depth", "depth >= 0"));
        builder.WithinSpace<Bot, Bot>(nameof(Bot.ParentBotId));
        builder.WithinSpace<Bot, Bot>(nameof(Bot.CreatedByBotId));
        builder.WithinSpace<Bot, ModelConnection>(nameof(Bot.ModelConnectionId));
        builder.ReferencesUser(nameof(Bot.CreatedByUserId));
    }

    #endregion
}
