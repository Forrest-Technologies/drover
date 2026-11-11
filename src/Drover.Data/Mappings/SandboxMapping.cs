namespace Drover.Data.Mappings;

public sealed class SandboxMapping : IEntityTypeConfiguration<Sandbox>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Sandbox> builder)
    {
        builder.ToTable("sandboxes");
        builder.HasKey(sandbox => sandbox.Id);
        builder.Property(sandbox => sandbox.Id).ValueGeneratedNever();
        builder.Property(sandbox => sandbox.Provider).HasMaxLength(64);
        builder.Property(sandbox => sandbox.ProviderSandboxId).HasMaxLength(200);
        builder.HasEnumColumn(sandbox => sandbox.Policy);
        builder.HasEnumColumn(sandbox => sandbox.State);
        builder.WithinSpace<Sandbox, Bot>(nameof(Sandbox.OwnerBotId));
    }

    #endregion
}
