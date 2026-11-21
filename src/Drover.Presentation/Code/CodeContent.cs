namespace Drover.Presentation.Code;

/// <summary>
/// What a block of code or output holds, decided once, so every view draws it the same way: code a fence or a card
/// names the language of, a shell command, JSON, a table, a diff, a log, bytes, or plain text. <see cref="Source"/> is
/// the text as it came, which is what is copied; <see cref="Text"/> is what is drawn, which differs only for JSON that
/// arrived on one line and is drawn indented. Reading is bounded: past <see cref="MaxDetectLength"/> characters a block
/// is plain text.
/// </summary>
public sealed record CodeContent(CodeKind Kind, string Source, string Text, string Label)
{
    #region Private Fields

    /// <summary>The longest text whose kind is looked for. A longer one is plain text.</summary>
    public const int MaxDetectLength = 1_048_576;

    private static readonly JsonWriterOptions indented = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    #endregion

    #region Properties

    /// <summary>The family of languages the text is highlighted as, or none.</summary>
    public CodeFamily Family { get; init; }

    /// <summary>The table, for a <see cref="CodeKind.Table"/>.</summary>
    public CodeTable? Table { get; init; }

    /// <summary>The bytes, for <see cref="CodeKind.Binary"/> and <see cref="CodeKind.Bytes"/>; empty for any other kind.</summary>
    public IReadOnlyList<byte> Bytes { get; init; } = [];

    #endregion

    #region Public Methods

    /// <summary>
    /// What the text holds. <paramref name="languageHint"/> is a fence's language or a file's extension; a hint it
    /// knows decides, and a word it does not know ("Output") is kept as the label of plain text.
    /// <paramref name="plainLabel"/> names plain text that has no hint.
    /// </summary>
    public static CodeContent Detect(string text, string? languageHint = null, string plainLabel = PlainLabel)
    {
        var hint = languageHint?.Trim() is { Length: > 0 } named ? named : null;
        var plain = new CodeContent(CodeKind.Plain, text, text, hint ?? plainLabel);

        if (text.Length > MaxDetectLength)
        {
            return plain;
        }

        return CodeLanguages.FamilyOf(hint) switch
        {
            CodeFamily.None => Detected(text) ?? plain,
            CodeFamily.Shell => Command(text, hint),
            CodeFamily.Json => Json(text) ?? plain with { Kind = CodeKind.Code, Family = CodeFamily.Json },
            CodeFamily.Diff => new(CodeKind.Diff, text, text, hint!) { Family = CodeFamily.Diff },
            CodeFamily.Table => CodeTable.Read(text, hasHeader: true) is { } table ? Tabled(text, table) : plain,
            var family => new(CodeKind.Code, text, text, hint!) { Family = family },
        };
    }

    /// <summary>A command for a shell, such as the command of a <c>sandbox.run</c> card.</summary>
    public static CodeContent Command(string text, string? shell = null) =>
        new(CodeKind.Shell, text, text, shell ?? ShellLabel) { Family = CodeFamily.Shell };

    #endregion

    #region Detection

    // Looked for in order, without a hint: JSON, a table, a diff, a log, bytes, and a bytes literal.
    private static CodeContent? Detected(string text)
    {
        if (Json(text) is { } json)
        {
            return json;
        }

        if (CodeTable.Read(text) is { } table)
        {
            return Tabled(text, table);
        }

        var lines = CodeLines.Split(text);

        if (CodeLines.IsDiff(lines))
        {
            return new(CodeKind.Diff, text, text, DiffLabel) { Family = CodeFamily.Diff };
        }

        if (CodeLines.IsLog(lines))
        {
            return new(CodeKind.Log, text, text, LogLabel);
        }

        if (CodeBytes.IsBinary(text))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return new(CodeKind.Binary, text, text, $"Binary output, {CodeBytes.Size(bytes.Length)}") { Bytes = bytes };
        }

        return CodeBytes.Literal(text) is { } literal
            ? new(CodeKind.Bytes, text, text, $"Bytes, {literal.Length.ToString("N0", CultureInfo.InvariantCulture)}") { Bytes = literal }
            : null;
    }

    // A JSON object or array, read within the display budget. One that arrived on one line is drawn indented.
    private static CodeContent? Json(string text)
    {
        if (text.TrimStart() is not ['{' or '[', ..] || ToolResults.Read(text) is not { Form: ResultForm.Whole } read
            || read.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return null;
        }

        var oneLine = !text.Trim().Contains('\n');
        var drawn = oneLine && !ToolResults.HasHalfSurrogate(text) ? Indented(read.Value) : text;
        return new(CodeKind.Json, text, drawn, JsonLabel) { Family = CodeFamily.Json };
    }

    private static CodeContent Tabled(string text, CodeTable table) =>
        new(CodeKind.Table, text, text, table.Label) { Family = CodeFamily.Table, Table = table };

    private static string Indented(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, indented))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    #endregion

    #region Fixed Words

    public const string PlainLabel = "Text";

    public const string ShellLabel = "bash";

    public const string JsonLabel = "JSON";

    public const string DiffLabel = "Diff";

    public const string LogLabel = "Log";

    #endregion
}
