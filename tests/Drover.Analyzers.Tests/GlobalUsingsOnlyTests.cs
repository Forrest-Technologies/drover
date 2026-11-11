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
            globalUsings: ("global using System;\n", "global using System;\nglobal using global::System.Text;\n"),
            source: (
                "{|DRV0002:using System.Text;|}\n\npublic class ThreadService\n{\n}\n",
                "\npublic class ThreadService\n{\n}\n"));

    [Fact]
    public Task Fix_preserves_a_namespace_relative_using() =>
        Verify.CodeFixWithGlobalUsings<GlobalUsingsOnlyAnalyzer, GlobalUsingsOnlyCodeFix>(
            globalUsings: (string.Empty, "global using global::Drover.Sample.Helpers;\n"),
            source: (
                """
                namespace Drover.Sample
                {
                    {|DRV0002:using Helpers;|}

                    public class Worker
                    {
                        public Helper Create() => new();
                    }
                }

                namespace Drover.Sample.Helpers
                {
                    public class Helper
                    {
                    }
                }
                """,
                """
                namespace Drover.Sample
                {

                    public class Worker
                    {
                        public Helper Create() => new();
                    }
                }

                namespace Drover.Sample.Helpers
                {
                    public class Helper
                    {
                    }
                }
                """));

    [Fact]
    public Task Fix_preserves_a_namespace_relative_static_using() =>
        Verify.CodeFixWithGlobalUsings<GlobalUsingsOnlyAnalyzer, GlobalUsingsOnlyCodeFix>(
            globalUsings: (string.Empty, "global using static global::Drover.Sample.Helpers.Values;\n"),
            source: (
                """
                namespace Drover.Sample
                {
                    {|DRV0002:using static Helpers.Values;|}

                    public class Worker
                    {
                        public int Read() => Answer;
                    }
                }

                namespace Drover.Sample.Helpers
                {
                    public static class Values
                    {
                        public const int Answer = 42;
                    }
                }
                """,
                """
                namespace Drover.Sample
                {

                    public class Worker
                    {
                        public int Read() => Answer;
                    }
                }

                namespace Drover.Sample.Helpers
                {
                    public static class Values
                    {
                        public const int Answer = 42;
                    }
                }
                """));

    [Fact]
    public Task Fix_reuses_an_existing_equivalent_global_using() =>
        Verify.CodeFixWithGlobalUsings<GlobalUsingsOnlyAnalyzer, GlobalUsingsOnlyCodeFix>(
            globalUsings: ("global using System.Text;\n", "global using System.Text;\n"),
            source: (
                "{|DRV0002:using System.Text;|}\n\npublic class ThreadService\n{\n}\n",
                "\npublic class ThreadService\n{\n}\n"));

    [Fact]
    public Task Fix_does_not_mistake_a_comment_for_an_existing_using() =>
        Verify.CodeFixWithGlobalUsings<GlobalUsingsOnlyAnalyzer, GlobalUsingsOnlyCodeFix>(
            globalUsings: (
                "// global using global::System.Text;\n",
                "// global using global::System.Text;\nglobal using global::System.Text;\n"),
            source: (
                "{|DRV0002:using System.Text;|}\n\npublic class ThreadService\n{\n}\n",
                "\npublic class ThreadService\n{\n}\n"));

    #endregion
}
