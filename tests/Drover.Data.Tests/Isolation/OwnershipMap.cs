namespace Drover.Data.Tests.Isolation;

/// <summary>A table, the class it belongs to, and the entity class mapped to it when there is one.</summary>
public sealed record TableOwnership(
    string Table,
    OwnershipClass Class,
    Type? Entity = null,
    IReadOnlyList<string>? ParentTables = null)
{
    public bool IsSpaceOwned => Class is OwnershipClass.Space or OwnershipClass.SpaceWithAudience;

    public IReadOnlyList<string> Parents => ParentTables ?? [];

    public bool IsTenantOwned => Class is not (OwnershipClass.Unclassified or OwnershipClass.Global);
}

/// <summary>
/// The class of every table, read from the entity model and <c>GlobalTables.manifest</c>. The catalog tests then
/// compare it with what the database holds, so a table that this map does not know is found.
/// </summary>
public sealed class OwnershipMap
{
    #region Private Fields

    private static readonly Lazy<OwnershipMap> current = new(ReadCurrent);

    #endregion

    #region Constructors

    private OwnershipMap(IReadOnlyList<TableOwnership> tables, IReadOnlyList<string> problems)
    {
        Tables = tables;
        Problems = problems;
    }

    #endregion

    #region Properties

    /// <summary>The map for the real model and the real manifest.</summary>
    public static OwnershipMap Current => current.Value;

    public IReadOnlyList<TableOwnership> Tables { get; }

    /// <summary>Things wrong with the model or the manifest themselves, before the database is looked at.</summary>
    public IReadOnlyList<string> Problems { get; }

    #endregion

    #region Public Methods

    public static OwnershipMap FromModel(IModel model, IReadOnlyList<GlobalTableEntry> globals)
    {
        var tables = new Dictionary<string, TableOwnership>();
        var problems = new List<string>();

        foreach (var entityType in model.GetEntityTypes().Where(type => type.GetTableName() is not null))
        {
            var table = entityType.GetTableName()!;

            if (!tables.ContainsKey(table))
            {
                tables[table] = Classify(entityType, table, globals, problems);
            }
        }

        AddGlobalTablesWithoutEntity(tables, globals);
        CheckEntityNames(model, globals, problems);
        return new OwnershipMap([.. tables.Values], problems);
    }

    public static OwnershipMap FromTables(IReadOnlyList<TableOwnership> tables) => new(tables, []);

    public OwnershipMap With(params TableOwnership[] more) => new([.. Tables, .. more], Problems);

    public IEnumerable<TableOwnership> InClass(params OwnershipClass[] classes) =>
        Tables.Where(table => classes.Contains(table.Class)).OrderBy(table => table.Table, StringComparer.Ordinal);

    public TableOwnership? Find(string table) => Tables.FirstOrDefault(found => found.Table == table);

    /// <summary>Tables of <paramref name="tableClass"/> that point at a table of one of <paramref name="parentClasses"/>.</summary>
    public IEnumerable<TableOwnership> InClassWithParentIn(OwnershipClass tableClass, params OwnershipClass[] parentClasses) =>
        InClass(tableClass).Where(table => table.Parents.Any(parent => Find(parent) is { } found && parentClasses.Contains(found.Class)));

    #endregion

    #region Private Methods

    private static OwnershipMap ReadCurrent()
    {
        var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        return FromModel(context.GetService<IDesignTimeModel>().Model, ManifestFile.ReadGlobalTables());
    }

    private static TableOwnership Classify(
        IEntityType entityType,
        string table,
        IReadOnlyList<GlobalTableEntry> globals,
        List<string> problems)
    {
        var entity = entityType.ClrType;
        var markers = new List<OwnershipClass>();
        var parents = entityType.GetForeignKeys()
            .Select(key => key.PrincipalEntityType.GetTableName())
            .OfType<string>()
            .Where(parent => parent != table)
            .Distinct()
            .ToList();

        if (entity.IsAssignableTo(typeof(ISpaceOwned)))
        {
            markers.Add(entity.IsAssignableTo(typeof(IAudienceScoped)) ? OwnershipClass.SpaceWithAudience : OwnershipClass.Space);
        }

        if (entity.IsAssignableTo(typeof(IUserOwned)))
        {
            markers.Add(OwnershipClass.User);
        }

        if (globals.Any(entry => entry.Table == table))
        {
            markers.Add(OwnershipClass.Global);
        }

        if (markers.Count > 1)
        {
            problems.Add($"Table '{table}' (entity {entity.Name}) has more than one class: {string.Join(", ", markers)}.");
        }

        return new TableOwnership(table, markers.FirstOrDefault(), entity, parents);
    }

    private static void AddGlobalTablesWithoutEntity(
        Dictionary<string, TableOwnership> tables,
        IReadOnlyList<GlobalTableEntry> globals)
    {
        foreach (var entry in globals.Where(entry => !tables.ContainsKey(entry.Table)))
        {
            tables[entry.Table] = new TableOwnership(entry.Table, OwnershipClass.Global);
        }
    }

    private static void CheckEntityNames(IModel model, IReadOnlyList<GlobalTableEntry> globals, List<string> problems)
    {
        foreach (var entry in globals.Where(entry => entry.Entity is not null))
        {
            var mapped = model.GetEntityTypes().FirstOrDefault(type => type.ClrType.Name == entry.Entity);

            if (mapped?.GetTableName() != entry.Table)
            {
                var actual = mapped?.GetTableName() ?? "nothing";
                problems.Add($"GlobalTables.manifest maps entity '{entry.Entity}' to '{entry.Table}'; the model maps it to '{actual}'.");
            }
        }
    }

    #endregion
}
