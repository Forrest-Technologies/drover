namespace Drover.Data.Repositories;

/// <summary>Stores one account-wide appearance choice inside a user tenant scope.</summary>
public sealed class AppearanceRepository(DroverDbContext db)
{
    #region Public Methods

    public Task<UserAppearance?> GetAppearance(CancellationToken cancellationToken) =>
        db.UserAppearances.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

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
        existing.Accent = appearance.Accent;
        existing.UpdatedAt = appearance.UpdatedAt;
    }

    #endregion
}
