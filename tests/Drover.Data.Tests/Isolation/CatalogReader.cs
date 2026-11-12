namespace Drover.Data.Tests.Isolation;

public sealed record ColumnSpec(string Name, string Type, bool NotNull);

public sealed record ForeignKeySpec(
    string Name,
    string Table,
    IReadOnlyList<string> Columns,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    bool CascadesUpdate)
{
    /// <summary>True when the key pairs <paramref name="column"/> of the child with the same column of the parent.</summary>
    public bool Includes(string column) =>
        Columns.Zip(ParentColumns).Any(pair => pair.First == column && pair.Second == column);
}

/// <summary>One table as <c>pg_catalog</c> describes it: columns, primary key, and foreign keys.</summary>
public sealed record TableShape(
    string Name,
    IReadOnlyList<ColumnSpec> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeySpec> ForeignKeys)
{
    public ColumnSpec? Column(string name) => Columns.FirstOrDefault(column => column.Name == name);
}

/// <summary>Reads the shape of every ordinary table in <c>public</c>. Partitions are left out: their parent speaks for them.</summary>
public static class CatalogReader
{
    #region Private Fields

    private const string TablesSql =
        """
        SELECT c.relname
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p') AND NOT c.relispartition
        ORDER BY c.relname
        """;

    private const string ColumnsSql =
        """
        SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p') AND NOT c.relispartition
          AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY c.relname, a.attnum
        """;

    private const string PrimaryKeysSql =
        """
        SELECT c.relname,
               ARRAY(SELECT a.attname::text FROM unnest(con.conkey) WITH ORDINALITY k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum ORDER BY k.ord)
        FROM pg_constraint con
        JOIN pg_class c ON c.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE con.contype = 'p' AND n.nspname = 'public'
        """;

    private const string ForeignKeysSql =
        """
        SELECT con.conname, child.relname, parent.relname, con.confupdtype = 'c',
               ARRAY(SELECT a.attname::text FROM unnest(con.conkey) WITH ORDINALITY k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum ORDER BY k.ord),
               ARRAY(SELECT a.attname::text FROM unnest(con.confkey) WITH ORDINALITY k(attnum, ord)
                     JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum ORDER BY k.ord)
        FROM pg_constraint con
        JOIN pg_class child ON child.oid = con.conrelid
        JOIN pg_class parent ON parent.oid = con.confrelid
        JOIN pg_namespace n ON n.oid = child.relnamespace
        WHERE con.contype = 'f' AND n.nspname = 'public'
        ORDER BY child.relname, con.conname
        """;

    #endregion

    #region Public Methods

    public static async Task<IReadOnlyList<TableShape>> Read(NpgsqlConnection connection)
    {
        var names = await ReadColumn(connection, TablesSql);
        var columns = await ReadColumns(connection);
        var primaryKeys = await ReadPrimaryKeys(connection);
        var foreignKeys = await ReadForeignKeys(connection);

        return
        [
            .. names.Select(name => new TableShape(
                name,
                columns.GetValueOrDefault(name) ?? [],
                primaryKeys.GetValueOrDefault(name) ?? [],
                [.. foreignKeys.Where(key => key.Table == name)])),
        ];
    }

    public static async Task<IReadOnlyList<ForeignKeySpec>> ReadForeignKeys(NpgsqlConnection connection)
    {
        var keys = new List<ForeignKeySpec>();
        await using var command = new NpgsqlCommand(ForeignKeysSql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            keys.Add(new ForeignKeySpec(
                reader.GetString(0),
                reader.GetString(1),
                await reader.GetFieldValueAsync<string[]>(4),
                reader.GetString(2),
                await reader.GetFieldValueAsync<string[]>(5),
                reader.GetBoolean(3)));
        }

        return keys;
    }

    #endregion

    #region Private Methods

    private static async Task<List<string>> ReadColumn(NpgsqlConnection connection, string sql)
    {
        var values = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<Dictionary<string, List<ColumnSpec>>> ReadColumns(NpgsqlConnection connection)
    {
        var columns = new Dictionary<string, List<ColumnSpec>>();
        await using var command = new NpgsqlCommand(ColumnsSql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            columns.TryAdd(table, []);
            columns[table].Add(new ColumnSpec(reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        }

        return columns;
    }

    private static async Task<Dictionary<string, string[]>> ReadPrimaryKeys(NpgsqlConnection connection)
    {
        var keys = new Dictionary<string, string[]>();
        await using var command = new NpgsqlCommand(PrimaryKeysSql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            keys[reader.GetString(0)] = await reader.GetFieldValueAsync<string[]>(1);
        }

        return keys;
    }

    #endregion
}
