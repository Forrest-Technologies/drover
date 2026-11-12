namespace Drover.Data.Mappings;

public static class JsonColumnExtensions
{
    #region Private Fields

    private static readonly JsonSerializerOptions columnJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // jsonb does not keep the order of keys, so the type discriminator of a message part may not come first.
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// Stores a record (or a list of records) from <c>Drover.Domain</c> in a <c>jsonb</c> column, in snake_case.
    /// Change tracking compares the JSON, so a record changed in place is still saved.
    /// </summary>
    public static PropertyBuilder<T> HasJsonRecord<T>(this PropertyBuilder<T> property, string? defaultSql = "'{}'::jsonb")
    {
        property.HasColumnType("jsonb").HasConversion(
            value => JsonSerializer.Serialize(value, columnJson),
            json => JsonSerializer.Deserialize<T>(json, columnJson)!,
            new ValueComparer<T>(
                (left, right) => JsonSerializer.Serialize(left, columnJson) == JsonSerializer.Serialize(right, columnJson),
                value => JsonSerializer.Serialize(value, columnJson).GetHashCode(),
                value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, columnJson), columnJson)!));

        if (defaultSql is not null)
        {
            property.HasDefaultValueSql(defaultSql);
        }

        return property;
    }

    #endregion
}
