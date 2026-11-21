namespace Drover.Data.Mappings;

public sealed class SandboxSecretMapping : IEntityTypeConfiguration<SandboxSecret>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<SandboxSecret> builder)
    {
        builder.ToTable("sandbox_secrets", table =>
        {
            table.HasCheckConstraint("ck_sandbox_secrets_name", $"name ~ '{SandboxSecretRules.NamePattern}'");
            table.HasCheckConstraint(
                "ck_sandbox_secrets_project",
                "(scope = 'project') = (project_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_sandbox_secrets_binding",
                "(mode = 'brokered' AND cardinality(hosts) > 0) OR (mode = 'raw' AND cardinality(hosts) = 0 AND scope <> 'project')");
        });
        builder.HasKey(secret => secret.Id);
        builder.Property(secret => secret.Id).ValueGeneratedNever();
        builder.Property(secret => secret.Name).HasMaxLength(SandboxSecretRules.MaxNameCharacters);
        builder.Property(secret => secret.Description).HasMaxLength(SandboxSecretRules.MaxDescriptionCharacters);
        builder.Property(secret => secret.Hosts).HasDefaultValueSql("'{}'::text[]");
        builder.Property(secret => secret.CreatedAt).HasDefaultValueSql("now()");
        builder.HasEnumColumn(secret => secret.Mode);
        builder.HasEnumColumn(secret => secret.Scope);
        builder.HasAlternateKey(secret => new { secret.SpaceId, secret.Id });

        // One value per name for the whole space, and one per name in each project. A name given to chosen bots is
        // kept unique per bot by sandbox_secret_bots.
        builder.HasIndex(secret => new { secret.SpaceId, secret.Name })
            .IsUnique()
            .HasFilter("scope = 'space'")
            .HasDatabaseName("ux_sandbox_secrets_space_name");
        builder.HasIndex(secret => new { secret.SpaceId, secret.ProjectId, secret.Name })
            .IsUnique()
            .HasFilter("scope = 'project'")
            .HasDatabaseName("ux_sandbox_secrets_project_name");
        builder.HasIndex(secret => secret.SecretId).IsUnique();
        builder.WithinSpace<SandboxSecret, Project>(nameof(SandboxSecret.ProjectId));
        builder.ReferencesUser(nameof(SandboxSecret.CreatedByUserId));
        builder.ReferencesUser(nameof(SandboxSecret.RotatedByUserId));
    }

    #endregion
}
