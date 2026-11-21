namespace Drover.Ui.Chat;

/// <summary>
/// A tool's result as a person reads it. The server stores every result as a JSON value, because the model reads it
/// that way: a command's outcome is an object, a tool's text is a JSON string. Shown as JSON, every tab and line break
/// inside a string reads as <c>\t</c> and <c>\n</c>, so a result is decoded here, once, for every view that shows a
/// card. A result whose start alone is known may be cut off anywhere, so its strings are read as far as they go.
/// A result is written by a tool, so reading it never fails and never costs more than a bounded budget: text that is
/// not JSON, is nested too deep, or is too long to read for display is shown as it came.
/// </summary>
public static class ToolResults
{
    #region Private Fields

    private static readonly JsonReaderOptions readerOptions = new() { MaxDepth = MaxReadDepth };

    // Shown as text on screen, never placed in markup or a script, so characters are written as themselves.
    private static readonly JsonWriterOptions indented = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] streams = ["stdout", "stderr"];

    #endregion

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

        var read = Read(result);
        return toolName switch
        {
            "sandbox.run" => CommandResult(read, result),
            "sandbox.read_file" when read is { Form: ResultForm.Whole, Value.ValueKind: JsonValueKind.Object } => FileResult(read.Value, result, arguments),
            _ => GeneralResult(read, result),
        };
    }

    /// <summary>
    /// What a command printed, from its result: <c>stdout</c> then <c>stderr</c>, decoded. A result cut off before its
    /// end still gives what it holds of each stream.
    /// </summary>
    public static ImmutableList<ToolOutputPiece> CommandOutput(string result)
    {
        var read = Read(result);
        var pieces = ImmutableList.CreateBuilder<ToolOutputPiece>();

        foreach (var stream in streams)
        {
            var text = read switch
            {
                { Form: ResultForm.Whole, Value.ValueKind: JsonValueKind.Object } => StringOf(read.Value, stream),
                { Form: ResultForm.Cut } => StringValueOf(result, stream),
                _ => null,
            };

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

    /// <summary>
    /// The result read as JSON for display, within the read budget. A result that opens an object, an array, or a
    /// string is read as a block with more to come: a whole value is <see cref="ResultForm.Whole"/>, the start of one
    /// is <see cref="ResultForm.Cut"/>. Anything else, including JSON that is invalid, nested deeper than
    /// <see cref="MaxReadDepth"/>, or longer than <see cref="MaxReadLength"/>, is <see cref="ResultForm.Text"/>.
    /// </summary>
    public static ReadResult Read(string result)
    {
        if (result.Length > MaxReadLength)
        {
            return ReadResult.AsText with { Reason = TooLong };
        }

        return result.TrimStart() is ['{' or '[' or '"', ..] ? Parsed(result) : ReadResult.AsText;
    }

    /// <summary>
    /// The text of a JSON string, or nothing when it is not a string. It is decoded from the string as written, so a
    /// string whose escapes name half of a surrogate pair, valid JSON that the platform refuses to read as text, still
    /// reads as what it says.
    /// </summary>
    public static string? StringOf(JsonElement value) =>
        value.ValueKind is JsonValueKind.String ? Unescaped(value.GetRawText(), 0) : null;

    /// <summary>The text of a string property of a JSON object, or nothing when it has none.</summary>
    public static string? StringOf(JsonElement value, string property) =>
        value.ValueKind is JsonValueKind.Object && value.TryGetProperty(property, out var found) ? StringOf(found) : null;

    /// <summary>
    /// True when an escape in the JSON names half of a surrogate pair. That is valid JSON and no valid text: the
    /// platform refuses to read it as a string or to write it out again, so such JSON is shown as it was written.
    /// </summary>
    public static bool HasHalfSurrogate(string json)
    {
        var awaitingLow = false;

        for (var index = 0; index < json.Length; index++)
        {
            if (json[index] is not '\\' || !TryUnit(json, index, out var unit))
            {
                if (awaitingLow)
                {
                    return true;
                }

                // An escape that is not a unit, such as \\ or \n, is passed over whole.
                index += json[index] is '\\' ? 1 : 0;
                continue;
            }

            if (char.IsLowSurrogate(unit) != awaitingLow)
            {
                return true;
            }

            awaitingLow = char.IsHighSurrogate(unit);
            index += 5;
        }

        return awaitingLow;
    }

    #endregion

    #region Kinds Of Result

    // A command's output and exit code are shown on their own. What is left is said in words: why it could not run or
    // finish, that the output was cut off, the note a new sandbox brings. The start of an object is told by its output
    // alone. A result that is not an object is shown as it came.
    private static ShownResult? CommandResult(ReadResult read, string result)
    {
        if (read is not { Form: ResultForm.Whole, Value.ValueKind: JsonValueKind.Object })
        {
            return read.Form is ResultForm.Cut && result.TrimStart().StartsWith('{') ? null : new(result, null);
        }

        var outcome = read.Value;
        List<string> lines = [];

        if (StringOf(outcome, "error") is { Length: > 0 } why)
        {
            lines.Add(why);
        }

        if (outcome.TryGetProperty("truncated", out var truncated) && truncated.ValueKind is JsonValueKind.True)
        {
            lines.Add(OutputCutOff);
        }

        if (StringOf(outcome, "note") is { Length: > 0 } words)
        {
            lines.Add(words);
        }

        return lines.Count == 0 ? null : new(string.Join('\n', lines), null);
    }

    // A file's text, in the language its name says.
    private static ShownResult FileResult(JsonElement file, string result, JsonElement arguments)
    {
        if (StringOf(file, "content") is not { } content)
        {
            return new(Laid(file, result), JsonLanguage);
        }

        var path = StringOf(file, "path") ?? StringOf(arguments, "path");
        return new(content, LanguageOfPath(path));
    }

    // Structured JSON is shown indented. It is read from the text as it came, which is never larger than the display
    // budget, and laid out only to be shown. Text holding an escape the platform will not write out again is shown as
    // it came.
    private static string Laid(JsonElement value, string result)
    {
        if (HasHalfSurrogate(result))
        {
            return result;
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, indented))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // A tool's text is a JSON string, shown as its value. Anything else structured is JSON, and labeled so.
    // A string that is no valid text, and a value that is not JSON, are shown as they came.
    private static ShownResult GeneralResult(ReadResult read, string result) =>
        read switch
        {
            { Form: ResultForm.Whole, Value.ValueKind: JsonValueKind.String } => new(StringOf(read.Value) ?? result, null),
            { Form: ResultForm.Whole, Value.ValueKind: JsonValueKind.Object or JsonValueKind.Array } => new(Laid(read.Value, result), JsonLanguage),
            { Form: ResultForm.Cut } when result.TrimStart().StartsWith('"') => new(Unescaped(result, result.IndexOf('"')), null),
            _ => new(result, null),
        };

    #endregion

    #region Reading

    // Reads text that opens a value as JSON. The reader says why a text is not JSON only by throwing, so its reason is
    // kept with the result that says to show the text as it came.
    private static ReadResult Parsed(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, new JsonReaderState(readerOptions));

        try
        {
            if (!JsonElement.TryParseValue(ref reader, out var parsed) || parsed is not { } whole)
            {
                return ReadResult.AsCut;
            }

            // One whole value is all a result may hold: anything after it but white space makes it no JSON at all.
            return OnlySpaceFrom(bytes, (int)reader.BytesConsumed) ? new(ResultForm.Whole, whole) : ReadResult.AsText with { Reason = TrailingText };
        }
        catch (JsonException exception)
        {
            // Not JSON after all, or nested deeper than the budget. Shown as it came, which is what the tool returned.
            return ReadResult.AsText with { Reason = exception.Message };
        }
    }

    private static bool OnlySpaceFrom(byte[] bytes, int start) =>
        bytes.AsSpan(start).IndexOfAnyExcept(" \t\r\n"u8) < 0;

    // The UTF-16 unit of a \u escape at index, which holds a backslash. False when it is no such escape.
    private static bool TryUnit(string json, int index, out char unit)
    {
        unit = default;

        if (index + 5 >= json.Length || json[index + 1] is not 'u'
            || !ushort.TryParse(json.AsSpan(index + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        unit = (char)code;
        return true;
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

    #region Constants

    /// <summary>The longest text read as JSON for display, a result or a call's arguments. A longer one is shown as it came.</summary>
    public const int MaxReadLength = 1_048_576;

    /// <summary>The deepest nesting read as JSON for display. A deeper result is shown as it came.</summary>
    public const int MaxReadDepth = 64;

    #endregion

    #region Fixed Words

    public const string JsonLanguage = "json";

    public const string OutputCutOff = "The output was cut off.";

    public const string TooLong = "The result is longer than is read for display.";

    public const string TrailingText = "The result holds text after its JSON value.";

    #endregion
}

/// <summary>A tool's result as text to show, and the language it is written in when known.</summary>
public sealed record ShownResult(string Text, string? Language);

/// <summary>How far a tool's result reads as JSON: as a whole value, as the start of one, or as plain text.</summary>
public enum ResultForm
{
    Text,
    Whole,
    Cut,
}

/// <summary>
/// A tool's result read for display: its <see cref="ResultForm"/>, the value when it is <see cref="ResultForm.Whole"/>,
/// and when it is <see cref="ResultForm.Text"/> for a reason worth knowing, the reason.
/// </summary>
public readonly record struct ReadResult(ResultForm Form, JsonElement Value, string? Reason = null)
{
    public static ReadResult AsText => new(ResultForm.Text, default);

    public static ReadResult AsCut => new(ResultForm.Cut, default);
}
