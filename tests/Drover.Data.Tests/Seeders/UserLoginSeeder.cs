namespace Drover.Data.Tests.Seeders;

public sealed class UserLoginSeeder : TableSeeder<UserLogin>
{
    public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
        {
            ["login_provider"] = "example",
            ["provider_key"] = seed.Unique("key"),
            ["provider_display_name"] = "Example",
        });
}
