namespace Drover.Presentation.Code;

/// <summary>
/// What one family of languages looks like to the highlighter: how its comments and strings are written, its
/// keywords and literal words, and whether a name before a colon is a key. One grammar serves each family; markup
/// has its own scanner.
/// </summary>
public sealed record CodeGrammar
{
    #region Private Fields

    private static readonly CodeGrammar json = new()
    {
        Quotes = "\"",
        KeyBeforeColon = true,
        Literals = Words("true false null"),
    };

    private static readonly CodeGrammar shell = new()
    {
        LineComments = ["#"],
        CommentAfterSpace = true,
        Variables = true,
        MultilineQuotes = "\"'",
        Keywords = Words("if then else elif fi for while until do done case esac in function return local export readonly unset set shift exit break continue select time source alias"),
        Literals = Words("true false"),
    };

    private static readonly CodeGrammar csharp = new()
    {
        LineComments = ["//"],
        BlockComment = ("/*", "*/"),
        PrefixedStrings = true,
        Keywords = Words(
            "abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern finally fixed float for foreach get goto if implicit in init int interface internal is lock long namespace new object operator out override params private protected public readonly record ref required return sbyte sealed set short sizeof stackalloc static string struct switch this throw try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where while with yield"),
        Literals = Words("true false null"),
    };

    private static readonly CodeGrammar javascript = new()
    {
        LineComments = ["//"],
        BlockComment = ("/*", "*/"),
        Quotes = "\"'`",
        MultilineQuotes = "`",
        Keywords = Words(
            "async await break case catch class const continue debugger default delete do else enum export extends finally for from function get if implements import in instanceof interface let new of private protected public readonly return set static super switch this throw try type typeof var void while with yield as declare keyof namespace"),
        Literals = Words("true false null undefined NaN Infinity"),
    };

    private static readonly CodeGrammar python = new()
    {
        LineComments = ["#"],
        TripleQuotes = true,
        Keywords = Words(
            "and as assert async await break class continue def del elif else except finally for from global if import in is lambda match case nonlocal not or pass raise return try while with yield self"),
        Literals = Words("True False None"),
    };

    private static readonly CodeGrammar yaml = new()
    {
        LineComments = ["#"],
        CommentAfterSpace = true,
        KeyBeforeColon = true,
        Literals = Words("true false null yes no on off ~"),
    };

    private static readonly CodeGrammar sql = new()
    {
        LineComments = ["--"],
        BlockComment = ("/*", "*/"),
        Quotes = "'\"",
        Keywords = Words(
            "select from where and or not in is like between join inner left right outer full cross on as group by order having limit offset union all distinct insert into values update set delete create alter drop table view index primary key foreign references default constraint unique check returning with case when then else end exists asc desc begin commit rollback grant revoke function returns language",
            ignoreCase: true),
        Literals = Words("null true false", ignoreCase: true),
    };

    private static readonly CodeGrammar css = new()
    {
        BlockComment = ("/*", "*/"),
        HyphenatedNames = true,
        KeyBeforeColon = true,
        Keywords = Words("@media @import @font-face @keyframes @supports @layer @container @charset @page !important"),
    };

    #endregion

    #region Properties

    public IReadOnlyList<string> LineComments { get; init; } = [];

    public (string Open, string Close)? BlockComment { get; init; }

    /// <summary>A <c>#</c> comment starts a line or follows a space, as in a shell, so <c>a#b</c> is one word.</summary>
    public bool CommentAfterSpace { get; init; }

    public string Quotes { get; init; } = "\"'";

    /// <summary>The quotes whose strings may run over line breaks.</summary>
    public string MultilineQuotes { get; init; } = string.Empty;

    /// <summary>Strings opened by three quotes, closed by the same three.</summary>
    public bool TripleQuotes { get; init; }

    /// <summary>A string led by <c>@</c> or <c>$</c>, as C# writes them, in which a doubled quote is a quote.</summary>
    public bool PrefixedStrings { get; init; }

    /// <summary>A <c>$NAME</c> or <c>${NAME}</c> is a variable, shown as a key.</summary>
    public bool Variables { get; init; }

    /// <summary>Names hold hyphens, and <c>!important</c> is a word, as in a style sheet.</summary>
    public bool HyphenatedNames { get; init; }

    /// <summary>A string or name followed by a colon is a key: JSON's, YAML's, and a style sheet's property names.</summary>
    public bool KeyBeforeColon { get; init; }

    public FrozenSet<string> Keywords { get; init; } = [];

    /// <summary>Words that are values, shown as numbers are: <c>true</c>, <c>null</c>, <c>None</c>.</summary>
    public FrozenSet<string> Literals { get; init; } = [];

    #endregion

    #region Public Methods

    /// <summary>The grammar a family is highlighted with, or nothing for a family drawn without one.</summary>
    public static CodeGrammar? For(CodeFamily family) => family switch
    {
        CodeFamily.Json => json,
        CodeFamily.Shell => shell,
        CodeFamily.CSharp => csharp,
        CodeFamily.JavaScript => javascript,
        CodeFamily.Python => python,
        CodeFamily.Yaml => yaml,
        CodeFamily.Sql => sql,
        CodeFamily.Css => css,
        _ => null,
    };

    #endregion

    #region Private Methods

    private static FrozenSet<string> Words(string words, bool ignoreCase = false) =>
        words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    #endregion
}
