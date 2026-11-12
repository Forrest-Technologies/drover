namespace Drover.Domain.Tenancy;

/// <summary>
/// A space-owned row with an audience: one user, or <see cref="Audience.Everyone"/> for the whole space.
/// </summary>
public interface IAudienceScoped
{
    Guid AudienceUserId { get; set; }
}
