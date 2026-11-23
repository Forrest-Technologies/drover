namespace Drover.Presentation.Tests.Code;

/// <summary>
/// The parts of a block drawn apart from plain text, for each family on a fixed sample. Punctuation is left out of
/// most expectations, since every family marks it the same way.
/// </summary>
public sealed class CodeHighlighterTests
{
    #region Families

    [Fact]
    public void Json_keys_strings_numbers_and_literals()
    {
        Words("""{"name": "Oslo", "n": -1.5e3, "ok": true, "x": null}""", CodeFamily.Json).Should().Equal(
            ("\"name\"", CodeTokenKind.Key),
            ("\"Oslo\"", CodeTokenKind.Quoted),
            ("\"n\"", CodeTokenKind.Key),
            ("1.5e3", CodeTokenKind.Number),
            ("\"ok\"", CodeTokenKind.Key),
            ("true", CodeTokenKind.Number),
            ("\"x\"", CodeTokenKind.Key),
            ("null", CodeTokenKind.Number));
    }

    [Fact]
    public void Shell_keywords_strings_variables_and_comments_after_a_space()
    {
        var script = "export NAME=\"$HOME/bin\" # note\nif [ -f a#b ]; then echo 'x\\y' $USER ${TAG}; fi\n";

        Words(script, CodeFamily.Shell).Should().Equal(
            ("export", CodeTokenKind.Keyword),
            ("\"$HOME/bin\"", CodeTokenKind.Quoted),
            ("# note", CodeTokenKind.Comment),
            ("if", CodeTokenKind.Keyword),
            ("then", CodeTokenKind.Keyword),
            ("'x\\y'", CodeTokenKind.Quoted),
            ("$USER", CodeTokenKind.Key),
            ("${TAG}", CodeTokenKind.Key),
            ("fi", CodeTokenKind.Keyword));
    }

    [Fact]
    public void CSharp_verbatim_and_interpolated_strings_comments_and_hex_numbers()
    {
        var code = "// greet\nvar name = @\"C:\\\"\"path\"\"\";\nif (count > 0x1F) { return $\"hi {name}\"; } /* done */\n";

        Words(code, CodeFamily.CSharp).Should().Equal(
            ("// greet", CodeTokenKind.Comment),
            ("var", CodeTokenKind.Keyword),
            ("@\"C:\\\"\"path\"\"\"", CodeTokenKind.Quoted),
            ("if", CodeTokenKind.Keyword),
            ("0x1F", CodeTokenKind.Number),
            ("return", CodeTokenKind.Keyword),
            ("$\"hi {name}\"", CodeTokenKind.Quoted),
            ("/* done */", CodeTokenKind.Comment));
    }

    [Fact]
    public void JavaScript_template_strings_run_over_line_breaks()
    {
        Words("const s = `a\nb`; // c\nlet n = 3.14;", CodeFamily.JavaScript).Should().Equal(
            ("const", CodeTokenKind.Keyword),
            ("`a\nb`", CodeTokenKind.Quoted),
            ("// c", CodeTokenKind.Comment),
            ("let", CodeTokenKind.Keyword),
            ("3.14", CodeTokenKind.Number));
    }

    [Fact]
    public void Python_triple_quoted_strings_literal_words_and_comments()
    {
        Words("def f(x):\n    \"\"\"doc\n    more\"\"\"\n    return None  # nothing\n", CodeFamily.Python).Should().Equal(
            ("def", CodeTokenKind.Keyword),
            ("\"\"\"doc\n    more\"\"\"", CodeTokenKind.Quoted),
            ("return", CodeTokenKind.Keyword),
            ("None", CodeTokenKind.Number),
            ("# nothing", CodeTokenKind.Comment));
    }

    [Fact]
    public void Yaml_keys_first_on_their_line_and_list_items()
    {
        Words("name: app\nitems:\n  - key: \"v\" # c\n  - plain: true\n", CodeFamily.Yaml).Should().Equal(
            ("name", CodeTokenKind.Key),
            ("items", CodeTokenKind.Key),
            ("key", CodeTokenKind.Key),
            ("\"v\"", CodeTokenKind.Quoted),
            ("# c", CodeTokenKind.Comment),
            ("plain", CodeTokenKind.Key),
            ("true", CodeTokenKind.Number));
    }

    [Fact]
    public void Sql_keywords_in_any_case_strings_numbers_and_comments()
    {
        Words("select count(*) FROM t where name = 'ab' and n > 10 -- note\n", CodeFamily.Sql).Should().Equal(
            ("select", CodeTokenKind.Keyword),
            ("FROM", CodeTokenKind.Keyword),
            ("where", CodeTokenKind.Keyword),
            ("'ab'", CodeTokenKind.Quoted),
            ("and", CodeTokenKind.Keyword),
            ("10", CodeTokenKind.Number),
            ("-- note", CodeTokenKind.Comment));
    }

    [Fact]
    public void Css_properties_rules_units_and_comments()
    {
        Words("a { color: red !important; } /* x */ @media (min-width: 360px) {}", CodeFamily.Css).Should().Equal(
            ("color", CodeTokenKind.Key),
            ("!important", CodeTokenKind.Keyword),
            ("/* x */", CodeTokenKind.Comment),
            ("@media", CodeTokenKind.Keyword),
            ("360px", CodeTokenKind.Number));
    }

    [Fact]
    public void Markup_tags_attributes_values_and_comments()
    {
        Parts("<!-- c --><a href=\"x\" data-id='1'>t</a>", CodeFamily.Markup).Should().Equal(
            ("<!-- c -->", CodeTokenKind.Comment),
            ("<", CodeTokenKind.Punctuation),
            ("a", CodeTokenKind.Keyword),
            ("href", CodeTokenKind.Key),
            ("=", CodeTokenKind.Punctuation),
            ("\"x\"", CodeTokenKind.Quoted),
            ("data-id", CodeTokenKind.Key),
            ("=", CodeTokenKind.Punctuation),
            ("'1'", CodeTokenKind.Quoted),
            (">", CodeTokenKind.Punctuation),
            ("</", CodeTokenKind.Punctuation),
            ("a", CodeTokenKind.Keyword),
            (">", CodeTokenKind.Punctuation));
    }

    [Fact]
    public void Punctuation_in_a_row_is_one_part_and_stops_where_a_comment_starts() =>
        Parts("f();// c", CodeFamily.CSharp).Should().Equal(("();", CodeTokenKind.Punctuation), ("// c", CodeTokenKind.Comment));

    #endregion

    #region Bounds

    [Theory]
    [InlineData("\"open string", CodeFamily.Json)]
    [InlineData("/* open comment", CodeFamily.CSharp)]
    [InlineData("<a href=\"open", CodeFamily.Markup)]
    [InlineData("x = '''open", CodeFamily.Python)]
    [InlineData("echo ${OPEN", CodeFamily.Shell)]
    public void A_string_comment_or_tag_left_open_runs_to_the_end_without_failing(string text, CodeFamily family)
    {
        var tokens = CodeHighlighter.Tokens(text, family);

        tokens.Should().NotBeEmpty();
        tokens[^1].Should().Match<CodeToken>(token => token.Start + token.Length == text.Length);
    }

    [Theory]
    [InlineData(CodeFamily.None)]
    [InlineData(CodeFamily.Diff)]
    [InlineData(CodeFamily.Table)]
    public void A_family_without_a_grammar_has_no_parts(CodeFamily family) => CodeHighlighter.Tokens("if (x) { }", family).Should().BeEmpty();

    [Fact]
    public void A_text_longer_than_the_layout_cap_is_drawn_plain() =>
        CodeHighlighter.Tokens(new string('1', CodeLines.MaxCharacters + 1), CodeFamily.Json).Should().BeEmpty();

    [Theory]
    [InlineData(CodeFamily.Json, """{"a": [1, "b", {"c": null}]}""")]
    [InlineData(CodeFamily.CSharp, "class A { int b = 1; string c = \"d\"; } // e")]
    [InlineData(CodeFamily.Markup, "<p class=\"a\">b<br/></p>")]
    [InlineData(CodeFamily.Shell, "for f in *.txt; do cat \"$f\" | wc -l; done")]
    public void Parts_are_in_order_never_overlap_and_stay_inside_the_text(CodeFamily family, string text)
    {
        var tokens = CodeHighlighter.Tokens(text, family);

        tokens.Should().OnlyContain(token => token.Length > 0 && token.Start >= 0 && token.Start + token.Length <= text.Length);
        tokens.Zip(tokens.Skip(1)).Should().OnlyContain(pair => pair.First.Start + pair.First.Length <= pair.Second.Start);
    }

    #endregion

    #region Private Methods

    private static List<(string Text, CodeTokenKind Kind)> Parts(string text, CodeFamily family) =>
        [.. CodeHighlighter.Tokens(text, family).Select(token => (text.Substring(token.Start, token.Length), token.Kind))];

    private static List<(string Text, CodeTokenKind Kind)> Words(string text, CodeFamily family) =>
        [.. Parts(text, family).Where(part => part.Kind is not CodeTokenKind.Punctuation)];

    #endregion
}
