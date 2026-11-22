namespace Drover.Engine.Appearance;

/// <summary>
/// The theme a person keeps for every device that follows their account. It is the person's in every space, so its
/// transactions set the person and no space. The server checks the shape of what it keeps, never which themes exist:
/// themes ship with the app, and a device that lacks one shows its default.
/// </summary>
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

        return kept is null ? null : new(kept.ThemeId, kept.Mode);
    }

    /// <summary>Keeps the theme for the person. False when it is not a theme id and a mode.</summary>
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
                UpdatedAt = time.GetUtcNow(),
            },
            cancellationToken);
        await work.Commit(cancellationToken);
        return true;
    }

    /// <summary>
    /// A theme id is lowercase words joined by hyphens, as a theme file's is, and the mode is one of three.
    /// </summary>
    public static bool IsWellFormed(AppearanceSetting? setting) =>
        setting is { ThemeId: { Length: > 0 and <= AppearanceSetting.MaxThemeIdLength } id, Mode: { } mode }
        && ThemeId().IsMatch(id)
        && AppearanceSetting.Modes.Contains(mode);

    #endregion

    #region Private Methods

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeId();

    #endregion
}
