namespace Drover.Domain.Files;

/// <summary>
/// Bytes held against a space's storage quota before a project upload writes them. The hold is the allowance the
/// upload may put in the file store. It is released, down to the stored size, when the file row is kept, or in full
/// when the upload stops.
/// </summary>
public sealed class FileByteReservation : ISpaceOwned
{
    #region Properties

    /// <summary>The id the file will have if the upload is kept.</summary>
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }

    public long Bytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    #endregion
}
