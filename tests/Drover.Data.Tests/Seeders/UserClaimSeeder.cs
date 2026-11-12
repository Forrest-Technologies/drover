namespace Drover.Data.Tests.Seeders;

public sealed class UserClaimSeeder : TableSeeder<UserClaim>
{
    public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
        {
            ["claim_type"] = seed.Unique("type"),
            ["claim_value"] = "value",
        });
}
