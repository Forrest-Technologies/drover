namespace Drover.Data.Tests.Seeders;

public sealed class UserAppearanceSeeder : PlainSeeder<UserAppearance>
{
    #region Protected Methods

    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["theme_id"] = "night-yard",
        ["mode"] = "black",
        ["accent"] = "blue",
        ["updated_at"] = DateTimeOffset.UtcNow,
    };

    #endregion
}
