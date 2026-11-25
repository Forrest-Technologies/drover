namespace Drover.Engine.Appearance;

/// <summary>Reads and validates the appearance choice owned by one person across every space.</summary>
public sealed partial class AppearanceService(
    AppearanceRepository appearances,
    TenantScope scope,
    TenantContext tenant,
    TimeProvider time)
{
    #region Public Methods

    public async Task<AppearanceSetting?> Get(Guid userId, CancellationToken cancellationToken)
    {
        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        var kept = await appearances.GetAppearance(cancellationToken);
        await work.Commit(cancellationToken);

        return kept is null ? null : new(kept.ThemeId, kept.Mode, kept.Accent);
    }

    public async Task<bool> Set(Guid userId, AppearanceSetting setting, CancellationToken cancellationToken)
    {
        if (!IsWellFormed(setting))
        {
            return false;
        }

        tenant.SetUser(userId);
        await using var work = await scope.Begin(cancellationToken);
        await appearances.SaveAppearance(
            new UserAppearance
            {
                UserId = userId,
                ThemeId = setting.ThemeId,
                Mode = setting.Mode,
                Accent = setting.Accent,
                UpdatedAt = time.GetUtcNow(),
            },
            cancellationToken);
        await work.Commit(cancellationToken);
        return true;
    }

    public static bool IsWellFormed(AppearanceSetting? setting) =>
        setting is
        {
            ThemeId: { Length: > 0 and <= AppearanceSetting.MaxThemeIdLength } themeId,
            Mode: { } mode,
            Accent: { Length: > 0 and <= AppearanceSetting.MaxAccentLength } accent,
        }
        && ThemeId().IsMatch(themeId)
        && AppearanceSetting.Modes.Contains(mode)
        && AppearanceSetting.Accents.Contains(accent);

    #endregion

    #region Private Methods

    [GeneratedRegex(@"\A[a-z]+(-[a-z]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeId();

    #endregion
}
