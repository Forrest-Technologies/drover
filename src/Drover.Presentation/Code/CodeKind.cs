namespace Drover.Presentation.Code;

/// <summary>What a block of code or output holds, which decides how it is drawn.</summary>
public enum CodeKind
{
    /// <summary>Anything else: monospaced, with its real tabs and line breaks.</summary>
    Plain,

    /// <summary>Code in a language the block was told, highlighted for it.</summary>
    Code,

    /// <summary>A command for a shell, after a quiet prompt mark.</summary>
    Shell,

    /// <summary>A JSON object or array.</summary>
    Json,

    /// <summary>Rows of tab or comma separated cells, drawn as a table.</summary>
    Table,

    /// <summary>A change to files, its added and removed lines marked.</summary>
    Diff,

    /// <summary>Lines that start with a time or a level word.</summary>
    Log,

    /// <summary>Text that is really bytes: summarized, with a hex view on demand.</summary>
    Binary,

    /// <summary>A bytes literal, such as <c>b'h\x1c'</c>: summarized, with a hex view on demand.</summary>
    Bytes,
}

/// <summary>The family of languages a block is highlighted as. One small tokenizer serves each.</summary>
public enum CodeFamily
{
    None,
    Json,
    Shell,
    CSharp,
    JavaScript,
    Python,
    Yaml,
    Markup,
    Sql,
    Css,
    Diff,
    Table,
}
