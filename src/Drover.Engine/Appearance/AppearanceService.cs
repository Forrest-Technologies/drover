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

    public static bool IsWellFormed(AppearanceSetting? setting) => ValidationErrors(setting).Count == 0;

    public static Dictionary<string, string[]> ValidationErrors(AppearanceSetting? setting)
    {
        Dictionary<string, string[]> errors = [];
        if (setting is null)
        {
            errors[nameof(AppearanceSetting.ThemeId)] = ["Choose a theme, mode, and accent."];
            return errors;
        }

        if (setting.ThemeId is not { Length: > 0 and <= AppearanceSetting.MaxThemeIdLength } themeId
            || !ThemeId().IsMatch(themeId))
        {
            errors[nameof(AppearanceSetting.ThemeId)] = ["Use a lowercase hyphenated theme id."];
        }

        if (setting.Mode is null || !AppearanceSetting.Modes.Contains(setting.Mode))
        {
            errors[nameof(AppearanceSetting.Mode)] = ["Choose a supported mode."];
        }

        if (setting.Accent is not { Length: > 0 and <= AppearanceSetting.MaxAccentLength } accent
            || !AppearanceSetting.Accents.Contains(accent))
        {
            errors[nameof(AppearanceSetting.Accent)] = ["Choose a supported accent."];
        }

        return errors;
    }

    #endregion

    #region Private Methods

    [GeneratedRegex(@"\A[a-z]+(-[a-z]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeId();

    #endregion
}
