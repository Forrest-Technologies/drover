namespace Drover.Data.Mappings;

public sealed class SandboxSecretBotMapping : IEntityTypeConfiguration<SandboxSecretBot>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<SandboxSecretBot> builder)
    {
        builder.ToTable("sandbox_secret_bots");
        builder.HasKey(row => new { row.SpaceId, row.SandboxSecretId, row.BotId });
        builder.Property(row => row.Name).HasMaxLength(SandboxSecretRules.MaxNameCharacters);

        // A bot is given at most one value under a name.
        builder.HasIndex(row => new { row.SpaceId, row.BotId, row.Name }).IsUnique();
        builder.WithinSpace<SandboxSecretBot, SandboxSecret>(nameof(SandboxSecretBot.SandboxSecretId)).OnDelete(DeleteBehavior.Cascade);
        builder.WithinSpace<SandboxSecretBot, Bot>(nameof(SandboxSecretBot.BotId));
    }

    #endregion
}
