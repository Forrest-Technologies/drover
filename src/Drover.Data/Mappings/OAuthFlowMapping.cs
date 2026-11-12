namespace Drover.Data.Mappings;

public sealed class OAuthFlowMapping : IEntityTypeConfiguration<OAuthFlow>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<OAuthFlow> builder)
    {
        builder.ToTable("oauth_flows");
        builder.HasKey(flow => flow.StateHash);
        builder.Property(flow => flow.StateHash).HasMaxLength(128).ValueGeneratedNever();
        builder.HasIndex(flow => flow.ExpiresAt);
        builder.WithinSpace<OAuthFlow, ToolConnection>(nameof(OAuthFlow.ToolConnectionId));
    }

    #endregion
}
