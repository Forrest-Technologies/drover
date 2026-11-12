namespace Drover.Analyzers.Tests;

/// <summary>DRV0004: no + concatenation involving a string.</summary>
public sealed class NoStringConcatenationTests
{
    #region Non-compliant samples

    [Fact]
    public Task Flags_a_string_plus_a_variable() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(int runId) => {|DRV0004:"Run " + runId|};
            }
            """);

    [Fact]
    public Task Flags_a_chain_once_for_the_whole_chain() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(int runId, int attempt) =>
                    {|DRV0004:"Run " + runId + " failed after " + attempt + " attempts"|};
            }
            """);

    [Fact]
    public Task Flags_two_string_variables() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(string first, string second) => {|DRV0004:first + second|};
            }
            """);

    [Fact]
    public Task Flags_concatenation_inside_parentheses_once() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(string name) => {|DRV0004:"Hello " + (name + "!")|};
            }
            """);

    [Fact]
    public Task Flags_compound_assignment_with_plus() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(string name)
                {
                    var greeting = "Hello ";
                    {|DRV0004:greeting += name|};
                    return greeting;
                }
            }
            """);

    #endregion

    #region Compliant samples

    [Fact]
    public Task Allows_concatenation_of_constants() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                private const string Prefix = "run-" + "id";

                public string Build() => "Hello " + "world" + Prefix;

                public string Name() => nameof(Messages) + ".Build";
            }
            """);

    [Fact]
    public Task Allows_interpolation() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(int runId, int attempt) => $"Run {runId} failed after {attempt} attempts";
            }
            """);

    [Fact]
    public Task Allows_addition_of_numbers() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public int Add(int first, int second) => first + second;

                public double Total(double first) => first + 1.5;

                public int FromChars(char first, char second) => first + second;
            }
            """);

    [Fact]
    public Task Allows_joining_through_library_calls() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Messages
            {
                public string Build(string first, string second) => string.Join("/", first, second);
            }
            """);

    #endregion

    #region Code fix

    [Fact]
    public Task Fix_rewrites_a_chain_as_one_interpolated_string() =>
        Verify.CodeFix<NoStringConcatenationAnalyzer, NoStringConcatenationCodeFix>(
            """
            public class Messages
            {
                public string Build(int runId, int attempt) =>
                    {|DRV0004:"Run " + runId + " failed after " + attempt + " attempts"|};
            }
            """,
            """
            public class Messages
            {
                public string Build(int runId, int attempt) =>
                    $"Run {runId} failed after {attempt} attempts";
            }
            """);

    [Fact]
    public Task Fix_escapes_quotes_and_braces_and_parenthesizes_a_conditional() =>
        Verify.CodeFix<NoStringConcatenationAnalyzer, NoStringConcatenationCodeFix>(
            """
            public class Messages
            {
                public string Build(bool ok) => {|DRV0004:"{\"state\": " + (ok ? "up" : "down") + "}"|};
            }
            """,
            """
            public class Messages
            {
                public string Build(bool ok) => $"{{\"state\": {(ok ? "up" : "down")}}}";
            }
            """);

    [Fact]
    public Task Fix_merges_a_parenthesized_concatenation() =>
        Verify.CodeFix<NoStringConcatenationAnalyzer, NoStringConcatenationCodeFix>(
            """
            public class Messages
            {
                public string Build(string name) => {|DRV0004:"Hello " + (name + "!")|};
            }
            """,
            """
            public class Messages
            {
                public string Build(string name) => $"Hello {name}!";
            }
            """);

    [Fact]
    public Task Allows_an_overloaded_addition_returning_string() =>
        Verify.Analyzer<NoStringConcatenationAnalyzer>("""
            public class Token
            {
                public static string operator +(Token token, string suffix) => "custom";
                public string Build() => this + "suffix";
            }
            """);

    [Theory]
    [InlineData("token + \"suffix\"", "token + \"suffix\"")]
    [InlineData("(token + \"suffix\")", "(token + \"suffix\")")]
    public Task Fix_preserves_nested_overloaded_addition(string operand, string interpolation) =>
        Verify.CodeFix<NoStringConcatenationAnalyzer, NoStringConcatenationCodeFix>(
            $$"""
            public class Token
            {
                public static string operator +(Token token, string suffix) => "custom";
                public string Build(Token token) => {|DRV0004:{{operand}} + "!"|};
            }
            """,
            $$"""
            public class Token
            {
                public static string operator +(Token token, string suffix) => "custom";
                public string Build(Token token) => $"{{{interpolation}}}!";
            }
            """);

    #endregion
}
