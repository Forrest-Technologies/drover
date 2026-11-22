namespace Drover.Data.Repositories;

/// <summary>
/// A person's theme for every device that follows their account. Runs inside a tenant scope the service has opened
/// with the person set: the policy shows a person only their own row.
/// </summary>
public sealed class AppearanceRepository(DroverDbContext db)
{
    #region Public Methods

    public Task<UserAppearance?> GetAppearance(CancellationToken cancellationToken) =>
        db.UserAppearances.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>Keeps the person's theme, replacing the one kept before.</summary>
    public async Task SaveAppearance(UserAppearance appearance, CancellationToken cancellationToken)
    {
        var existing = await db.UserAppearances.SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            db.UserAppearances.Add(appearance);
            return;
        }

        existing.ThemeId = appearance.ThemeId;
        existing.Mode = appearance.Mode;
        existing.UpdatedAt = appearance.UpdatedAt;
    }

    #endregion
}
