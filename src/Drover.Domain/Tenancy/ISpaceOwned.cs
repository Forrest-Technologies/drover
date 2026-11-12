namespace Drover.Domain.Tenancy;

/// <summary>A row that belongs to one space. The database shows it only to a transaction running for that space.</summary>
public interface ISpaceOwned
{
    Guid SpaceId { get; set; }
}
