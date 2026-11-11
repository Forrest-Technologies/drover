namespace Drover.Data.Mappings;

/// <summary>Stores an enum as its snake_case name, the way the data model document spells it: <c>pending_approval</c>.</summary>
public sealed class SnakeCaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(
        value => SnakeCase.Of(value),
        text => Enum.Parse<TEnum>(text.Replace("_", string.Empty), true))
    where TEnum : struct, Enum
{
}
