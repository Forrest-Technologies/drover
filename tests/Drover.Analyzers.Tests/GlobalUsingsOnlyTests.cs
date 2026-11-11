namespace Drover.Analyzers.Tests;

/// <summary>DRV0002: no local using directives.</summary>
public sealed class GlobalUsingsOnlyTests
{
    #region Non-compliant samples

    [Fact]
    public Task Flags_a_using_directive_at_the_top_of_a_file() =>
        Verify.Analyzer<GlobalUsingsOnlyAnalyzer>("""
            {|DRV0002:using System;|}

            public class ThreadService
            {
            }
            """);

    [Fact]
    public Task Flags_a_using_directive_inside_a_namespace() =>
        Verify.Analyzer<GlobalUsingsOnlyAnalyzer>("""
            namespace Drover.Sample
            {
                {|DRV0002:using System.Text;|}

                public class ThreadService
                {
                }
            }
            """);

    [Fact]
    public Task Flags_a_using_static_directive_and_each_plain_directive() =>
        Verify.Analyzer<GlobalUsingsOnlyAnalyzer>("""
            {|DRV0002:using System.IO;|}
            {|DRV0002:using static System.Math;|}

            public class ThreadService
            {
            }
            """);

    [Fact]
    public Task Flags_a_directive_in_a_file_that_is_not_named_GlobalUsings() =>
        Verify.AnalyzerFile<GlobalUsingsOnlyAnalyzer>("Usings.cs", "{|DRV0002:using System;|}\n");

    #endregion

    #region Compliant samples

    [Fact]
    public Task Allows_a_global_using_directive() =>
        Verify.Analyzer<GlobalUsingsOnlyAnalyzer>("""
            global using System;
            global using System.Text;

            public class ThreadService
            {
            }
            """);

    [Fact]
    public Task Allows_an_alias_directive() =>
        Verify.Analyzer<GlobalUsingsOnlyAnalyzer>("""
            using Json = System.Text.Json.JsonSerializer;

            public class ThreadService
            {
            }
            """);

    [Fact]
    public Task Allows_any_directive_in_GlobalUsings_dot_cs() =>
        Verify.AnalyzerFile<GlobalUsingsOnlyAnalyzer>("GlobalUsings.cs", "using System;\n");

    [Fact]
    public Task Allows_using_statements_and_using_declarations() =>
        Verify.Analyzer<GlobalUsingsOnlyAnalyzer>("""
            public class ThreadService
            {
                public void Run(System.IDisposable first, System.IDisposable second)
                {
                    using (first)
                    {
                    }

                    using var scope = second;
                }
            }
            """);

    #endregion

    #region Code fix

    [Fact]
    public Task Fix_moves_the_directive_to_the_existing_GlobalUsings_file() =>
        Verify.CodeFixWithGlobalUsings<GlobalUsingsOnlyAnalyzer, GlobalUsingsOnlyCodeFix>(
            globalUsings: ("global using System;\n", "global using System;\nglobal using System.Text;\n"),
            source: (
                "{|DRV0002:using System.Text;|}\n\npublic class ThreadService\n{\n}\n",
                "\npublic class ThreadService\n{\n}\n"));

    #endregion
}
