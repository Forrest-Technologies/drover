namespace Drover.Data.Tests;

/// <summary>Every entity that belongs to a space or a user registers a seeder; the generated tests cannot run without one.</summary>
public sealed class SeederCoverageTests
{
    #region Public Methods

    [Fact]
    public void Every_space_and_user_owned_entity_has_exactly_one_seeder()
    {
        var problems = SeederRegistry.Problems(OwnershipMap.Current, SeederRegistry.Discover());

        problems.Should().BeEmpty();
    }

    [Fact]
    public void Every_seeder_belongs_to_an_entity_in_the_model()
    {
        var modelled = OwnershipMap.Current.Tables.Select(table => table.Entity).ToList();

        var orphans = SeederRegistry.Discover().Where(seeder => !modelled.Contains(seeder.Entity));

        orphans.Select(seeder => seeder.GetType().Name).Should().BeEmpty();
    }

    #endregion
}
