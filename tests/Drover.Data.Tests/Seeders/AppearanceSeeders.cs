namespace Drover.Data.Tests.Seeders;

public sealed class UserAppearanceSeeder : PlainSeeder<UserAppearance>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["theme_id"] = "night-yard",
        ["mode"] = "dark",
        ["updated_at"] = DateTimeOffset.UtcNow,
    };
}
