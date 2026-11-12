namespace Drover.Domain.Tenancy;

/// <summary>A row that belongs to one user, whatever space they are in.</summary>
public interface IUserOwned
{
    Guid UserId { get; set; }
}
