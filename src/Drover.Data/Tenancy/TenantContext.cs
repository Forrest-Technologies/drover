namespace Drover.Data.Tenancy;

/// <summary>
/// The space and user one request or one job is running for. Set once, by middleware after the membership check
/// or by the worker for a job. It refuses to change space, so code that runs later in the same scope cannot
/// move to another tenant.
/// </summary>
public sealed class TenantContext
{
    #region Properties

    public Guid? SpaceId { get; private set; }

    public Guid? UserId { get; private set; }

    #endregion

    #region Public Methods

    public void SetUser(Guid userId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);

        if (UserId is { } current && current != userId)
        {
            throw new InvalidOperationException("The user of a tenant context cannot change once it is set.");
        }

        UserId = userId;
    }

    public void SetSpace(Guid spaceId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(spaceId, Guid.Empty);

        if (SpaceId is { } current && current != spaceId)
        {
            throw new InvalidOperationException("The space of a tenant context cannot change once it is set.");
        }

        SpaceId = spaceId;
    }

    public void Set(Guid spaceId, Guid userId)
    {
        SetUser(userId);
        SetSpace(spaceId);
    }

    #endregion
}
