namespace Drover.Presentation.Tests.Code;

/// <summary>What a block holds, decided once for every view: each kind, its near misses, and what is copied.</summary>
public sealed class CodeContentTests
{
    #region Private Fields

    private const string Rows = "chain\tname\tprice\nsolana\tCYBERLEEK\t0.002139\nbase\tTOSHI\t0.000412\n";

    #endregion

    #region Kinds

    [Fact]
    public void Tab_separated_rows_are_a_table_named_by_its_row_count()
    {
        var content = CodeContent.Detect(Rows);

        content.Kind.Should().Be(CodeKind.Table);
        content.Label.Should().Be("Table, 2 rows");
        content.Table!.Header.Should().Equal("chain", "name", "price");
    }

    [Fact]
    public void One_line_json_is_drawn_indented_and_copied_as_it_came()
    {
        var oneLine = """{"city":"Oslo","hours":[1,2],"note":"é"}""";

        var content = CodeContent.Detect(oneLine);

        content.Kind.Should().Be(CodeKind.Json);
        content.Label.Should().Be("JSON");
        content.Text.Should().Contain("\n").And.Contain("\"city\": \"Oslo\"").And.Contain("é", "letters are kept, not escaped");
        content.Source.Should().Be(oneLine);
    }

    [Fact]
    public void Json_that_is_already_indented_keeps_its_layout()
    {
        var laidOut = "{\n    \"a\": 1\n}";

        CodeContent.Detect(laidOut).Text.Should().Be(laidOut);
    }

    [Theory]
    [InlineData("{\"a\":1,}")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    public void Text_that_is_no_json_object_or_array_is_not_json(string text) =>
        CodeContent.Detect(text).Kind.Should().NotBe(CodeKind.Json);

    [Fact]
    public void A_diff_is_a_diff()
    {
        var diff = "diff --git a/x.cs b/x.cs\nindex 1..2 100644\n--- a/x.cs\n+++ b/x.cs\n@@ -1,2 +1,2 @@\n context\n-old\n+new\n";

        CodeContent.Detect(diff).Kind.Should().Be(CodeKind.Diff);
    }

    [Fact]
    public void A_log_is_a_log()
    {
        var log = "2026-11-20T10:00:01Z INFO started\n2026-11-20T10:00:02Z WARN slow disk\n2026-11-20T10:00:03Z ERROR failed to bind\n";

        var content = CodeContent.Detect(log);

        content.Kind.Should().Be(CodeKind.Log);
        content.Label.Should().Be("Log");
    }

    [Fact]
    public void Bytes_printed_by_a_program_are_summarized_by_their_size()
    {
        var content = CodeContent.Detect("PK\u0003\u0004\0\0\u0014\0binary");

        content.Kind.Should().Be(CodeKind.Binary);
        content.Label.Should().Be($"Binary output, {content.Bytes.Count} bytes");
    }

    [Fact]
    public void A_bytes_literal_is_summarized_by_its_byte_count()
    {
        var content = CodeContent.Detect("b'h\\x1c\\xfeK'\n");

        content.Kind.Should().Be(CodeKind.Bytes);
        content.Bytes.Should().Equal([0x68, 0x1c, 0xfe, 0x4b]);
        content.Label.Should().Be("Bytes, 4");
    }

    [Fact]
    public void Anything_else_is_plain_text_named_by_its_label()
    {
        var content = CodeContent.Detect("sunny\tdry", "Output");

        content.Kind.Should().Be(CodeKind.Plain);
        content.Label.Should().Be("Output");
        CodeContent.Detect("hello").Label.Should().Be(CodeContent.PlainLabel);
    }

    #endregion

    #region Hints

    [Theory]
    [InlineData("cs", CodeFamily.CSharp)]
    [InlineData("Python", CodeFamily.Python)]
    [InlineData("tsx", CodeFamily.JavaScript)]
    [InlineData("yml", CodeFamily.Yaml)]
    [InlineData("html", CodeFamily.Markup)]
    [InlineData("sql", CodeFamily.Sql)]
    [InlineData("scss", CodeFamily.Css)]
    public void A_known_language_is_code_highlighted_for_its_family(string language, CodeFamily family)
    {
        var content = CodeContent.Detect("{ x }", language);

        content.Kind.Should().Be(CodeKind.Code);
        content.Family.Should().Be(family);
        content.Label.Should().Be(language);
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("sh")]
    [InlineData("console")]
    public void A_shell_fence_is_a_shell_command(string shell) =>
        CodeContent.Detect("ls -la", shell).Kind.Should().Be(CodeKind.Shell);

    [Fact]
    public void A_command_is_a_shell_command_named_bash() =>
        CodeContent.Command("curl -s https://example.test").Should().Match<CodeContent>(content => content.Kind == CodeKind.Shell && content.Label == "bash");

    [Fact]
    public void An_unknown_language_draws_as_plain_text_under_its_name()
    {
        var content = CodeContent.Detect("PRINT 1", "basic-ish");

        content.Kind.Should().Be(CodeKind.Plain);
        content.Label.Should().Be("basic-ish");
    }

    [Fact]
    public void A_json_fence_that_does_not_parse_is_still_highlighted_as_json()
    {
        var content = CodeContent.Detect("{ \"a\": 1, // note\n}", "json");

        content.Kind.Should().Be(CodeKind.Code);
        content.Family.Should().Be(CodeFamily.Json);
    }

    [Fact]
    public void A_csv_fence_reads_its_first_row_as_the_header()
    {
        var content = CodeContent.Detect("1,2\n3,4\n", "csv");

        content.Kind.Should().Be(CodeKind.Table);
        content.Table!.Header.Should().Equal("1", "2");
        content.Label.Should().Be("Table, 1 row");
    }

    [Fact]
    public void A_text_longer_than_the_detect_budget_is_plain_text()
    {
        var huge = string.Concat(Enumerable.Repeat("a\tb\n", (CodeContent.MaxDetectLength / 4) + 1));

        CodeContent.Detect(huge).Kind.Should().Be(CodeKind.Plain);
    }

    #endregion
}
