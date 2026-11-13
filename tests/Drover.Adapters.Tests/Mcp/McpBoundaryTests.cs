namespace Drover.Adapters.Tests.Mcp;

/// <summary>
/// The protocol library is referenced by the adapter and by nothing else that ships, and none of its types appears
/// in anything the adapter shows the rest of Drover.
/// </summary>
public sealed partial class McpBoundaryTests
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

    [Fact]
    public void No_log_call_in_the_adapter_is_handed_a_failure_which_may_hold_what_a_tool_server_sent()
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "Drover.Adapters.Mcp");
        var offenders = Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (File: Path.GetFileName(file), Line: index + 1, Text: line)))
            .Where(line => LogCallWithFailure().IsMatch(line.Text))
            .Select(line => $"{line.File}:{line.Line}")
            .ToList();

        offenders.Should().BeEmpty("a failure from the protocol library can carry the server's own text, or a header; only its type is logged");
    }

    #endregion

    #region Private Methods

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Drover.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test binaries.");
    }

    [GeneratedRegex(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)\(\s*(failure|exception|error|gone|\w*Exception)\s*,")]
    private static partial Regex LogCallWithFailure();

    #endregion
}
