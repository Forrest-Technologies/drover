namespace Drover.Domain.Spaces;

public sealed class Membership : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public required Guid UserId { get; set; }

    public required MembershipRole Role { get; set; }

    #endregion
}
