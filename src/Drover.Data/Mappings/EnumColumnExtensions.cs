namespace Drover.Data.Mappings;

public static class EnumColumnExtensions
{
    #region Public Methods

    /// <summary>
    /// Maps an enum property to a text column holding its snake_case name, and adds a check constraint that names
    /// every allowed value, so the database refuses a value the enum does not have. Call after <c>ToTable</c>.
    /// </summary>
    public static PropertyBuilder<TEnum> HasEnumColumn<TEntity, TEnum>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TEnum>> property,
        int maxLength = 32)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var column = SnakeCase.Of(((MemberExpression)property.Body).Member.Name);
        var allowed = string.Join(", ", Enum.GetValues<TEnum>().Select(value => $"'{SnakeCase.Of(value)}'"));
        var constraint = $"ck_{builder.Metadata.GetTableName()}_{column}";
        builder.ToTable(table => table.HasCheckConstraint(constraint, $"{column} IN ({allowed})"));
        return builder.Property(property).HasConversion<SnakeCaseEnumConverter<TEnum>>().HasMaxLength(maxLength);
    }

    #endregion
}
