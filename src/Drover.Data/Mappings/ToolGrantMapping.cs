namespace Drover.Data.Mappings;

public sealed class ToolGrantMapping : IEntityTypeConfiguration<ToolGrant>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ToolGrant> builder)
    {
        builder.ToTable("tool_grants");
        builder.HasKey(grant => new { grant.SpaceId, grant.BotId, grant.ToolId });
        builder.HasEnumColumn(grant => grant.Effect);
        builder.HasEnumColumn(grant => grant.ReviewState);
        builder.WithinSpace<ToolGrant, Bot>(nameof(ToolGrant.BotId));
        builder.WithinSpace<ToolGrant, Tool>(nameof(ToolGrant.ToolId));
        builder.WithinSpace<ToolGrant, Bot>(nameof(ToolGrant.GrantedByBotId));
        builder.ReferencesUser(nameof(ToolGrant.GrantedByUserId));
    }

    #endregion
}
