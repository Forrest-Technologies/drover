namespace Drover.Adapters.Tests.Mcp;

/// <summary>
/// The protocol library is referenced by the adapter and by nothing else that ships, and none of its types appears
/// in anything the adapter shows the rest of Drover.
/// </summary>
public sealed class McpBoundaryTests
{
    #region Private Fields

    private const string Library = "ModelContextProtocol";

    #endregion

    #region Tests

    [Fact]
    public void Only_the_adapter_references_the_protocol_library()
    {
        var adapter = typeof(McpToolSource).Assembly;
        Type[] elsewhere =
        [
            typeof(IToolSource),
            typeof(Drover.Contracts.Tools.ToolConnectionSummary),
            typeof(DroverDbContext),
            typeof(Drover.Agents.Runtime.AgentRuntime),
        ];

        adapter.GetReferencedAssemblies().Should().Contain(assembly => assembly.Name!.StartsWith(Library, StringComparison.Ordinal));

        foreach (var type in elsewhere)
        {
            type.Assembly.GetReferencedAssemblies().Should().NotContain(
                assembly => assembly.Name!.StartsWith(Library, StringComparison.Ordinal),
                $"{type.Assembly.GetName().Name} must not know the protocol library");
        }
    }

    [Fact]
    public void Nothing_the_adapter_makes_public_carries_a_type_of_the_protocol_library()
    {
        var reachable = typeof(McpToolSource).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            .SelectMany(member => member switch
            {
                System.Reflection.MethodBase method => method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Concat(method is System.Reflection.MethodInfo returning ? [returning.ReturnType] : []),
                System.Reflection.PropertyInfo property => [property.PropertyType],
                System.Reflection.FieldInfo field => [field.FieldType],
                _ => [],
            })
            .SelectMany(type => type.IsGenericType ? [type, .. type.GetGenericArguments()] : new[] { type });

        reachable.Should().NotContain(type => type.Namespace != null && type.Namespace.StartsWith(Library, StringComparison.Ordinal));
    }

    #endregion
}
