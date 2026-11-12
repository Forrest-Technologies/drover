namespace Drover.Domain.Tests;

/// <summary>
/// No vendor type and no framework type in any entity or setting record. Every type a public property of
/// <c>Drover.Domain</c> or <c>Drover.Contracts</c> uses is a .NET base type or Drover's own.
/// </summary>
public sealed class ProviderNeutralityTests
{
    #region Private Fields

    private static readonly string[] allowedNamespaces = ["System", "Drover"];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_property_type_in_the_domain_and_contracts_is_a_base_type_or_Drovers_own()
    {
        var assemblies = new[] { typeof(Bot).Assembly, typeof(GrantEffect).Assembly };

        var foreign = assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetProperties().Select(property => (type, property)))
            .SelectMany(pair => NamespacesOf(pair.property.PropertyType).Select(space => $"{pair.type.Name}.{pair.property.Name}: {space}"))
            .Where(entry => !allowedNamespaces.Any(allowed => IsIn(entry.Split(": ")[1], allowed)))
            .ToList();

        foreign.Should().BeEmpty();
    }

    [Fact]
    public void The_domain_and_contracts_reference_no_vendor_assembly()
    {
        var references = new[] { typeof(Bot).Assembly, typeof(GrantEffect).Assembly }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name ?? string.Empty)
            .Distinct()
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Drover", StringComparison.Ordinal)
                && name != "netstandard"
                && name != "mscorlib")
            .ToList();

        references.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static IEnumerable<string> NamespacesOf(Type type)
    {
        var element = type.HasElementType ? type.GetElementType()! : type;
        yield return element.Namespace ?? string.Empty;

        foreach (var argument in element.IsGenericType ? element.GetGenericArguments() : [])
        {
            foreach (var space in NamespacesOf(argument))
            {
                yield return space;
            }
        }
    }

    private static bool IsIn(string space, string root) =>
        space == root || space.StartsWith($"{root}.", StringComparison.Ordinal);

    #endregion
}
