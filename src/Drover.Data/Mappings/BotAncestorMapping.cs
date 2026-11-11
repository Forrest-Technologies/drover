namespace Drover.Data.Mappings;

public sealed class BotAncestorMapping : IEntityTypeConfiguration<BotAncestor>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<BotAncestor> builder)
    {
        builder.ToTable("bot_ancestors");
        builder.HasKey(row => new { row.SpaceId, row.DescendantBotId, row.AncestorBotId });
        builder.HasIndex(row => new { row.SpaceId, row.AncestorBotId });
        builder.ToTable(table => table.HasCheckConstraint("ck_bot_ancestors_distance", "distance >= 0"));
        builder.WithinSpace<BotAncestor, Bot>(nameof(BotAncestor.AncestorBotId));
        builder.WithinSpace<BotAncestor, Bot>(nameof(BotAncestor.DescendantBotId));
    }

    #endregion
}
