namespace Drover.Ui.Chat;

/// <summary>
/// A tool's result as a person reads it. The server stores every result as a JSON value, because the model reads it
/// that way: a command's outcome is an object, a tool's text is a JSON string. Shown as JSON, every tab and line break
/// inside a string reads as <c>\t</c> and <c>\n</c>, so a result is decoded here, once, for every view that shows a
/// card. A result whose start alone is known may be cut off anywhere, so its strings are read as far as they go.
/// </summary>
public static class ToolResults
{
    #region Public Methods

    /// <summary>
    /// The text a card shows as its result, and the language it is written in when that is known, or nothing when the
    /// card says all of it elsewhere: a command's output is its Output part, and its exit code a line of its own.
    /// </summary>
    public static ShownResult? Shown(string toolName, string result, JsonElement arguments)
    {
        if (result.Length == 0)
        {
            return null;
        }

        var parsed = Parsed(result);
        return toolName switch
        {
            "sandbox.run" => CommandResult(parsed, result),
            "sandbox.read_file" when parsed is { ValueKind: JsonValueKind.Object } file => FileResult(file, result, arguments),
            _ => GeneralResult(parsed, result),
        };
    }

    /// <summary>
    /// What a command printed, from its result: <c>stdout</c> then <c>stderr</c>, decoded. A result cut off before its
    /// end still gives what it holds of each stream.
    /// </summary>
    public static ImmutableList<ToolOutputPiece> CommandOutput(string result)
    {
        var parsed = Parsed(result);
        var pieces = ImmutableList.CreateBuilder<ToolOutputPiece>();

        foreach (var stream in new[] { "stdout", "stderr" })
        {
            var text = parsed is { ValueKind: JsonValueKind.Object } whole
                ? whole.TryGetProperty(stream, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null
                : StringValueOf(result, stream);

            if (text is { Length: > 0 })
            {
                pieces.Add(new ToolOutputPiece(stream, text));
            }
        }

        return pieces.ToImmutable();
    }

    /// <summary>
    /// The value of a JSON string that begins at <paramref name="start"/>, an opening quote, with its escapes decoded.
    /// When the text ends before the closing quote, what came before is the value.
    /// </summary>
    public static string Unescaped(string json, int start)
    {
        var value = new StringBuilder();

        for (var index = start + 1; index < json.Length; index++)
        {
            var character = json[index];

            if (character is '"')
            {
                break;
            }

            if (character is not '\\')
            {
                value.Append(character);
                continue;
            }

            if (index + 1 >= json.Length || !TryEscape(json, ref index, value))
            {
                break;
            }
        }

        return value.ToString();
    }

    #endregion

    #region Kinds Of Result

    // A command's output and exit code are shown on their own. What is left is said in words: why it could not run or
    // finish, that the output was cut off, the note a new sandbox brings. A result that is not an object is shown as it
    // came.
    private static ShownResult? CommandResult(JsonElement? parsed, string result)
    {
        if (parsed is not { ValueKind: JsonValueKind.Object } outcome)
        {
            return result.TrimStart().StartsWith('{') ? null : new(result, null);
        }

        List<string> lines = [];

        if (outcome.TryGetProperty("error", out var error) && error.ValueKind is JsonValueKind.String && error.GetString() is { Length: > 0 } why)
        {
            lines.Add(why);
        }

        if (outcome.TryGetProperty("truncated", out var truncated) && truncated.ValueKind is JsonValueKind.True)
        {
            lines.Add(OutputCutOff);
        }

        if (outcome.TryGetProperty("note", out var note) && note.ValueKind is JsonValueKind.String && note.GetString() is { Length: > 0 } words)
        {
            lines.Add(words);
        }

        return lines.Count == 0 ? null : new(string.Join('\n', lines), null);
    }

    // A file's text, in the language its name says.
    private static ShownResult FileResult(JsonElement file, string result, JsonElement arguments)
    {
        if (!file.TryGetProperty("content", out var content) || content.ValueKind is not JsonValueKind.String)
        {
            return new(result, JsonLanguage);
        }

        var path = file.TryGetProperty("path", out var named) && named.ValueKind is JsonValueKind.String
            ? named.GetString()
            : arguments.ValueKind is JsonValueKind.Object && arguments.TryGetProperty("path", out var asked) && asked.ValueKind is JsonValueKind.String
                ? asked.GetString()
                : null;
        return new(content.GetString() ?? string.Empty, LanguageOfPath(path));
    }

    // A tool's text is a JSON string, shown as its value. Anything else structured is JSON, and labeled so.
    private static ShownResult GeneralResult(JsonElement? parsed, string result) =>
        parsed switch
        {
            { ValueKind: JsonValueKind.String } text => new(text.GetString() ?? string.Empty, null),
            { ValueKind: JsonValueKind.Object or JsonValueKind.Array } => new(result, JsonLanguage),
            null when result.TrimStart().StartsWith('"') => new(Unescaped(result, result.IndexOf('"')), null),
            _ => new(result, null),
        };

    #endregion

    #region Reading

    // A whole JSON value, or nothing when the text is cut off or is not JSON. A result is always JSON or the start of
    // it, so only a text that opens an object, an array, or a string is read, and it is read as a block with more to
    // come, where a cut off value is simply not whole yet.
    private static JsonElement? Parsed(string result)
    {
        if (result.TrimStart() is not ['{' or '[' or '"', ..])
        {
            return null;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(result), isFinalBlock: false, state: default);
        return JsonElement.TryParseValue(ref reader, out var parsed) ? parsed : null;
    }

    // A string property read from JSON that may be cut off, as far as it goes.
    private static string? StringValueOf(string json, string property)
    {
        var found = Regex.Match(json, $"\"{Regex.Escape(property)}\"\\s*:\\s*\"");
        return found.Success ? Unescaped(json, found.Index + found.Length - 1) : null;
    }

    // Decodes the escape at index, which holds a backslash, and moves past it. False when the escape is cut off.
    private static bool TryEscape(string json, ref int index, StringBuilder value)
    {
        var code = json[++index];

        if (code is 'u')
        {
            if (index + 4 >= json.Length
                || !int.TryParse(json.AsSpan(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unit))
            {
                return false;
            }

            value.Append((char)unit);
            index += 4;
            return true;
        }

        value.Append(code switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            'b' => '\b',
            'f' => '\f',
            _ => code,
        });
        return true;
    }

    private static string? LanguageOfPath(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        return extension.Length > 1 ? extension[1..].ToLowerInvariant() : null;
    }

    #endregion

    #region Fixed Words

    public const string JsonLanguage = "json";

    public const string OutputCutOff = "The output was cut off.";

    #endregion
}


/// <summary>A tool's result as text to show, and the language it is written in when known.</summary>
public sealed record ShownResult(string Text, string? Language);
