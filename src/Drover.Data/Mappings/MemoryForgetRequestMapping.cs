namespace Drover.Data.Mappings;

public sealed class MemoryForgetRequestMapping : IEntityTypeConfiguration<MemoryForgetRequest>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<MemoryForgetRequest> builder)
    {
        builder.ToTable("memory_forget_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.Kind)
            .HasConversion<LowercaseEnumConverter<MemoryForgetKind>>()
            .HasMaxLength(32);
        builder.Property(request => request.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(request => new { request.SpaceId, request.AttemptedAt, request.CreatedAt, request.Id });

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_memory_forget_requests_kind",
            "kind IN ('conversation', 'person')"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_memory_forget_requests_scope",
            "(kind = 'conversation' AND bot_id IS NOT NULL AND thread_id IS NOT NULL) OR (kind = 'person' AND bot_id IS NULL AND thread_id IS NULL)"));
    }

    #endregion
}
