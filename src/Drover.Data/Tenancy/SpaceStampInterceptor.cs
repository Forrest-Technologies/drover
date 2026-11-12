namespace Drover.Data.Tenancy;

/// <summary>
/// Fills <see cref="ISpaceOwned.SpaceId"/> on new entities from the tenant context. A convenience only: the
/// <c>WITH CHECK</c> clause of the policy is what rejects a row for another space.
/// </summary>
public sealed class SpaceStampInterceptor(TenantContext tenant) : SaveChangesInterceptor
{
    #region Public Methods

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampNewEntities(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampNewEntities(eventData.Context);
        return ValueTask.FromResult(result);
    }

    #endregion

    #region Private Methods

    private void StampNewEntities(DbContext? context)
    {
        if (context is null || tenant.SpaceId is not { } spaceId)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ISpaceOwned>())
        {
            if (entry.State is EntityState.Added && entry.Entity.SpaceId == Guid.Empty)
            {
                entry.Entity.SpaceId = spaceId;
            }
        }
    }

    #endregion
}
