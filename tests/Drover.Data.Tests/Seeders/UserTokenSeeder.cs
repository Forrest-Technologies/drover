namespace Drover.Data.Tests.Seeders;

public sealed class UserTokenSeeder : TableSeeder<UserToken>
{
    public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
        {
            ["login_provider"] = "example",
            ["name"] = seed.Unique("token"),
            ["value"] = "value",
        });
}
