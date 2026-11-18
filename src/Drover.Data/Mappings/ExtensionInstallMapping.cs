namespace Drover.Data.Mappings;

public sealed class ExtensionInstallMapping : IEntityTypeConfiguration<ExtensionInstall>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ExtensionInstall> builder)
    {
        builder.ToTable("extension_installs");
        builder.HasKey(install => install.Id);
        builder.Property(install => install.Id).ValueGeneratedNever();
        builder.Property(install => install.PackageId).HasMaxLength(100);
        builder.Property(install => install.Name).HasMaxLength(200);
        builder.Property(install => install.Description).HasMaxLength(2000);
        builder.Property(install => install.ContentHash).HasMaxLength(64);
        builder.Property(install => install.Package).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.HasIndex(install => new { install.SpaceId, install.PackageId }).IsUnique();
        builder.WithinSpace<ExtensionInstall, Bot>(nameof(ExtensionInstall.BotId));
        builder.WithinSpace<ExtensionInstall, ToolConnection>(nameof(ExtensionInstall.ToolConnectionId));
        builder.ReferencesUser(nameof(ExtensionInstall.InstalledByUserId));
    }

    #endregion
}
