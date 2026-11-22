namespace Drover.Data.Mappings;

public sealed class SandboxSecretEntryMapping : IEntityTypeConfiguration<SandboxSecretEntry>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<SandboxSecretEntry> builder)
    {
        builder.ToTable("sandbox_secret_entries");
        builder.HasKey(entry => new { entry.SpaceId, entry.SandboxId, entry.SandboxSecretId });

        // A placeholder stands for one name in one sandbox: a bot's value, or the values of a name's projects and the
        // space. The broker looks it up among the entries of the sandbox that sent it.
        builder.HasIndex(entry => new { entry.SpaceId, entry.SandboxId, entry.PlaceholderHash }).HasFilter("placeholder_hash IS NOT NULL");
        builder.WithinSpace<SandboxSecretEntry, Sandbox>(nameof(SandboxSecretEntry.SandboxId)).OnDelete(DeleteBehavior.Cascade);

        // A secret removed stops at once: the broker finds no entry for its placeholder.
        builder.WithinSpace<SandboxSecretEntry, SandboxSecret>(nameof(SandboxSecretEntry.SandboxSecretId)).OnDelete(DeleteBehavior.Cascade);
    }

    #endregion
}
