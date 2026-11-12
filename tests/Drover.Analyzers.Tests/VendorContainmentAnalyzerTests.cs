namespace Drover.Analyzers.Tests;

public sealed class VendorContainmentAnalyzerTests
{
    #region Private Fields

    /// <summary>Stand-ins for the vendor namespaces, so no vendor package is needed to compile the samples.</summary>
    private const string VendorStubs =
        """
        namespace Microsoft.Agents.AI { public class AIAgent { } }
        namespace Microsoft.Agents.AI.Workflows { public class Workflow { } }
        namespace Microsoft.Extensions.AI { public interface IChatClient { } }
        namespace ModelContextProtocol.Client { public class McpClient { } }
        namespace Microsoft.Extensions.Logging { public interface ILogger { } }
        """;

    #endregion

    #region Non-Compliant

    [Fact]
    public async Task A_framework_type_outside_Drover_Agents_is_reported()
    {
        var diagnostics = await Run(
            "Drover.Engine",
            """
            public sealed class RunWorker
            {
                public Microsoft.Agents.AI.AIAgent? Agent { get; set; }
            }
            """);

        var reported = diagnostics.Should().ContainSingle().Which;
        reported.Id.Should().Be("DRV0007");
        reported.Severity.Should().Be(DiagnosticSeverity.Error);
        reported.GetMessage().Should().Contain("Microsoft.Agents.AI").And.Contain("Drover.Agents");
    }

    [Fact]
    public async Task A_using_directive_for_a_vendor_namespace_is_reported()
    {
        var diagnostics = await Run(
            "Drover.Domain",
            """
            using Microsoft.Agents.AI.Workflows;

            public sealed class Team
            {
                public Workflow? Shape { get; set; }
            }
            """);

        diagnostics.Should().HaveCount(2).And.OnlyContain(diagnostic => diagnostic.Id == "DRV0007");
    }

    [Fact]
    public async Task An_MCP_type_outside_the_MCP_adapter_is_reported()
    {
        var diagnostics = await Run(
            "Drover.Agents",
            """
            public sealed class Tools
            {
                public ModelContextProtocol.Client.McpClient? Client { get; set; }
            }
            """);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("Drover.Adapters.Mcp");
    }

    #endregion

    #region Compliant

    [Fact]
    public async Task The_framework_inside_Drover_Agents_is_allowed()
    {
        var diagnostics = await Run(
            "Drover.Agents",
            """
            using Microsoft.Agents.AI;

            public sealed class Runtime
            {
                public AIAgent? Agent { get; set; }
                public Microsoft.Extensions.AI.IChatClient? Client { get; set; }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task The_server_wiring_may_reference_every_vendor()
    {
        var diagnostics = await Run(
            "Drover.Server",
            """
            public sealed class Wiring
            {
                public Microsoft.Agents.AI.AIAgent? Agent { get; set; }
                public ModelContextProtocol.Client.McpClient? Client { get; set; }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_projects_and_platform_namespaces_are_not_vendors()
    {
        var inTests = await Run("Drover.Adapters.Tests", "public sealed class Probe { public Microsoft.Agents.AI.AIAgent? Agent { get; set; } }");
        var platform = await Run("Drover.Engine", "public sealed class Worker { public Microsoft.Extensions.Logging.ILogger? Logger { get; set; } }");

        inTests.Should().BeEmpty();
        platform.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static Task<ImmutableArray<Diagnostic>> Run(string assemblyName, string source) =>
        AnalyzerHarness.Run(new VendorContainmentAnalyzer(), $"{source}\n{VendorStubs}", assemblyName: assemblyName);

    #endregion
}
