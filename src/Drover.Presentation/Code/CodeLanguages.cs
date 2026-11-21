namespace Drover.Presentation.Code;

/// <summary>
/// The family a language name belongs to: a fence's language, or a file's extension. A name it does not know, or a
/// word that names what a block is rather than its language ("Output"), belongs to none.
/// </summary>
public static class CodeLanguages
{
    #region Private Fields

    private static readonly FrozenDictionary<string, CodeFamily> families = new Dictionary<string, CodeFamily>(StringComparer.OrdinalIgnoreCase)
    {
        ["json"] = CodeFamily.Json,
        ["jsonc"] = CodeFamily.Json,
        ["bash"] = CodeFamily.Shell,
        ["sh"] = CodeFamily.Shell,
        ["shell"] = CodeFamily.Shell,
        ["zsh"] = CodeFamily.Shell,
        ["console"] = CodeFamily.Shell,
        ["cs"] = CodeFamily.CSharp,
        ["csharp"] = CodeFamily.CSharp,
        ["c#"] = CodeFamily.CSharp,
        ["js"] = CodeFamily.JavaScript,
        ["jsx"] = CodeFamily.JavaScript,
        ["mjs"] = CodeFamily.JavaScript,
        ["cjs"] = CodeFamily.JavaScript,
        ["javascript"] = CodeFamily.JavaScript,
        ["ts"] = CodeFamily.JavaScript,
        ["tsx"] = CodeFamily.JavaScript,
        ["typescript"] = CodeFamily.JavaScript,
        ["py"] = CodeFamily.Python,
        ["python"] = CodeFamily.Python,
        ["yaml"] = CodeFamily.Yaml,
        ["yml"] = CodeFamily.Yaml,
        ["xml"] = CodeFamily.Markup,
        ["html"] = CodeFamily.Markup,
        ["htm"] = CodeFamily.Markup,
        ["svg"] = CodeFamily.Markup,
        ["xaml"] = CodeFamily.Markup,
        ["csproj"] = CodeFamily.Markup,
        ["razor"] = CodeFamily.Markup,
        ["sql"] = CodeFamily.Sql,
        ["css"] = CodeFamily.Css,
        ["scss"] = CodeFamily.Css,
        ["diff"] = CodeFamily.Diff,
        ["patch"] = CodeFamily.Diff,
        ["csv"] = CodeFamily.Table,
        ["tsv"] = CodeFamily.Table,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Methods

    /// <summary>The family of a language name, or <see cref="CodeFamily.None"/> for a name it does not know.</summary>
    public static CodeFamily FamilyOf(string? language) =>
        language is { Length: > 0 } named && families.TryGetValue(named.Trim(), out var family) ? family : CodeFamily.None;

    #endregion
}
