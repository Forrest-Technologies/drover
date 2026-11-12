namespace Drover.Data.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresFixtureDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
