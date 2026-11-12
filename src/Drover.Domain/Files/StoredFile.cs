namespace Drover.Domain.Files;

/// <summary>Metadata of a stored file. The bytes live behind the file store, under <see cref="StorageKey"/>.</summary>
public sealed class StoredFile : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public required string StorageKey { get; set; }

    public required string Name { get; set; }

    public required string ContentType { get; set; }

    public long Size { get; set; }

    /// <summary>Lowercase hex.</summary>
    public required string Sha256 { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? CreatedByBotId { get; set; }

    #endregion
}
