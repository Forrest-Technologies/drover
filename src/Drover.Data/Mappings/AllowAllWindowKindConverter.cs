namespace Drover.Data.Mappings;

/// <summary>
/// The two timed kinds are spelled with a number in the data model (<c>minutes_30</c>, <c>hour_1</c>), which the
/// general snake-case converter cannot produce from a PascalCase member name. The names live with the enum in the
/// domain, so the audit log writes the same words the database stores.
/// </summary>
public sealed class AllowAllWindowKindConverter()
    : ValueConverter<AllowAllWindowKind, string>(
        kind => AllowAllWindowKindNames.Of(kind),
        text => AllowAllWindowKindNames.Read(text));
