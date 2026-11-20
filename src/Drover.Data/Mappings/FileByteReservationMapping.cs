namespace Drover.Data.Mappings;

public sealed class FileByteReservationMapping : IEntityTypeConfiguration<FileByteReservation>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<FileByteReservation> builder)
    {
        builder.ToTable("file_byte_reservations");
        builder.HasKey(hold => hold.Id);
        builder.Property(hold => hold.Id).ValueGeneratedNever();
        builder.Property(hold => hold.CreatedAt).HasDefaultValueSql("now()");
        builder.ToTable(table => table.HasCheckConstraint("ck_file_byte_reservations_bytes", "bytes > 0"));
    }

    #endregion
}
