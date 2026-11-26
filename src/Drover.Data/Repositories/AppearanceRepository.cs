namespace Drover.Data.Repositories;

/// <summary>Stores one account-wide appearance choice inside a user tenant scope.</summary>
public sealed class AppearanceRepository(DroverDbContext db)
{
    #region Public Methods

    public Task<UserAppearance?> GetAppearance(CancellationToken cancellationToken) =>
        db.UserAppearances.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public async Task SaveAppearance(UserAppearance appearance, CancellationToken cancellationToken)
    {
        var lockKey = $"user-appearance:{appearance.UserId}";
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);

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
