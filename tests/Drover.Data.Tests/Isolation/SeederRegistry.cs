namespace Drover.Data.Tests.Isolation;

/// <summary>Finds the seeders and compares them with the tables that need one.</summary>
public static class SeederRegistry
{
    #region Public Methods

    /// <summary>Every seeder class in this test assembly. Seeders nested in a test belong to that test alone.</summary>
    public static IReadOnlyList<TableSeeder> Discover() =>
    [
        .. typeof(SeederRegistry).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsNested: false } && type.IsAssignableTo(typeof(TableSeeder)))
            .Select(type => (TableSeeder)Activator.CreateInstance(type)!),
    ];

    /// <summary>
    /// One message per tenant table that has no seeder, or more than one. Global tables need none: their policies
    /// are written by hand and covered by named tests.
    /// </summary>
    public static IReadOnlyList<string> Problems(OwnershipMap ownership, IEnumerable<TableSeeder> seeders)
    {
        var problems = new List<string>();
        var registered = seeders.ToList();

        foreach (var table in ownership.Tables.Where(table => table.IsTenantOwned))
        {
            var count = registered.Count(seeder => seeder.Entity == table.Entity);

            if (count == 0)
            {
                problems.Add($"Entity {table.Entity?.Name} (table '{table.Table}') has no seeder. Add a TableSeeder<{table.Entity?.Name}>.");
            }
            else if (count > 1)
            {
                problems.Add($"Entity {table.Entity?.Name} (table '{table.Table}') has {count} seeders; it needs exactly one.");
            }
        }

        return problems;
    }

    #endregion
}
