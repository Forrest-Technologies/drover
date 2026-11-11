namespace Drover.Data.Mappings;

/// <summary>Stores an enum as its lowercase name, the way the data model document spells it: owner, admin, member.</summary>
public sealed class LowercaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(
        value => value.ToString().ToLowerInvariant(),
        text => Enum.Parse<TEnum>(text, true))
    where TEnum : struct, Enum
{
}
