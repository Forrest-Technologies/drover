namespace Drover.Data.Mappings;

public sealed class BotRuleMapping : IEntityTypeConfiguration<BotRule>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<BotRule> builder)
    {
        builder.ToTable("bot_rules", table => table.HasCheckConstraint("ck_bot_rules_position", "position >= 0"));
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.Name).HasMaxLength(100);
        builder.Property(rule => rule.Conditions).HasJsonRecord("'[]'::jsonb");
        builder.HasEnumColumn(rule => rule.Checkpoint);
        builder.HasEnumColumn(rule => rule.Effect);
        builder.WithinSpace<BotRule, Bot>(nameof(BotRule.BotId));
        builder.ReferencesUser(nameof(BotRule.CreatedByUserId));
    }

    #endregion
}
