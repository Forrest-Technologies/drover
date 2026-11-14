namespace Drover.Ui.Chat;

/// <summary>
/// Words for tool calls: what a call did in plain terms, its arguments on one line, and its result. Drover's own
/// tools are named in words; any other tool goes by the title its definition gives, or by its name.
/// </summary>
public static partial class ToolCards
{
    #region Private Fields

    // Shown as text on screen, never placed in markup or a script, so characters are written as themselves.
    private static readonly JsonSerializerOptions indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, ToolWords> builtIn = Known(
        ("sandbox.run", new("Running a command", "Ran a command", "run a command")),
        ("sandbox.read_file", new("Reading a file", "Read a file", "read a file")),
        ("sandbox.write_file", new("Writing a file", "Wrote a file", "write a file")),
        ("sandbox.list_files", new("Listing files", "Listed files", "list files")),
        ("computer.screenshot", new("Taking a screenshot", "Took a screenshot", "take a screenshot")),
        ("computer.click", new("Clicking", "Clicked", "click")),
        ("computer.type", new("Typing", "Typed", "type")),
        ("computer.key", new("Pressing keys", "Pressed keys", "press keys")),
        ("computer.scroll", new("Scrolling", "Scrolled", "scroll")),
        ("time.now", new("Checking the time", "Checked the time", "check the time")));

    #endregion

    #region Public Methods

    /// <summary>
    /// The card for a call. <paramref name="result"/> is the call's stored result part, when the thread has it: its
    /// whole result, where the call's events hold only the start of it.
    /// </summary>
    public static ToolCard ToCard(this ToolCallView call, PartView? result = null)
    {
        var phase = PhaseOf(call);
        var words = WordsFor(call);
        var arguments = JsonElement.Parse(call.Arguments);
        var summary = Summarize(Canonical(call.ToolName), arguments);

        // A stored result is JSON. The preview in the call's events is the start of it, which may be cut off.
        var resultText = result?.Result is { Length: > 0 } stored ? Pretty(JsonElement.Parse(stored)) : call.Preview;

        return new(
            call.ToolCallId,
            phase,
            LabelFor(words, phase),
            words.Ask,
            summary,
            Pretty(arguments),
            CoversAll(arguments, summary),
            resultText.Length == 0 ? null : resultText,
            ExitCodeIn(resultText),
            call.Pieces);
    }

    /// <summary>
    /// The call as the stored messages tell it, for a call whose events this screen does not have: it ended as its
    /// result says, or has not started when there is no result yet.
    /// </summary>
    public static ToolCallView FromParts(PartView call, PartView? result) =>
        new(
            call.ToolCallId ?? Guid.Empty,
            call.ToolName ?? string.Empty,
            call.ToolName ?? string.Empty,
            call.Arguments ?? "{}",
            "allow",
            result is null ? "requested" : result.IsError ? "failed" : "completed",
            result?.IsError ?? false);

    #endregion

    #region Words

    private static ToolPhase PhaseOf(ToolCallView call) => call.State switch
    {
        "requested" => ToolPhase.Starting,
        "awaiting_approval" => ToolPhase.WaitingForYou,
        "approved" => ToolPhase.Approved,
        "running" => ToolPhase.Running,
        "completed" => ToolPhase.Done,
        "failed" => ToolPhase.Failed,
        "denied" => ToolPhase.Denied,
        "refused" => ToolPhase.NotAllowed,
        "interrupted" => ToolPhase.Interrupted,
        "cancelled" => ToolPhase.Stopped,
        _ => call.IsError ? ToolPhase.Failed : ToolPhase.Done,
    };

    private static string LabelFor(ToolWords words, ToolPhase phase) => phase switch
    {
        ToolPhase.Starting or ToolPhase.Approved or ToolPhase.Running => words.Doing,
        ToolPhase.WaitingForYou or ToolPhase.Denied or ToolPhase.NotAllowed => Capitalized(words.Ask),
        _ => words.Done,
    };

    private static ToolWords WordsFor(ToolCallView call)
    {
        if (builtIn.TryGetValue(call.ToolName, out var words))
        {
            return words;
        }

        var name = string.IsNullOrWhiteSpace(call.Title) ? call.ToolName : call.Title.Trim();
        return new(name, name, $"use {name}");
    }

    private static string Capitalized(string words) =>
        words.Length == 0 ? words : $"{char.ToUpperInvariant(words[0])}{words[1..]}";

    // A model sees a tool's name with its dots as underscores, and a stored call part keeps that spelling.
    private static string Canonical(string toolName) =>
        builtIn.Keys.FirstOrDefault(name => name.Replace('.', '_') == toolName) ?? toolName;

    private static Dictionary<string, ToolWords> Known(params (string Name, ToolWords Words)[] tools)
    {
        var known = tools.ToDictionary(tool => tool.Name, tool => tool.Words, StringComparer.Ordinal);

        foreach (var (name, words) in tools)
        {
            known[name.Replace('.', '_')] = words;
        }

        return known;
    }

    #endregion

    #region Arguments And Result

    private static string Summarize(string toolName, JsonElement values)
    {
        if (values.ValueKind is not JsonValueKind.Object)
        {
            return OneLine(values.GetRawText());
        }

        return toolName switch
        {
            "sandbox.run" => OneLine(Text(values, "command")),
            "sandbox.read_file" or "sandbox.write_file" or "sandbox.list_files" => Text(values, "path"),
            "computer.screenshot" => string.Empty,
            "computer.click" => $"{Text(values, "x")}, {Text(values, "y")}",
            "computer.type" => OneLine(Text(values, "text")),
            "computer.key" => values.TryGetProperty("keys", out var keys) && keys.ValueKind is JsonValueKind.Array
                ? string.Join("+", keys.EnumerateArray().Select(key => key.ToString()))
                : string.Empty,
            "computer.scroll" => $"{Text(values, "direction")} {Text(values, "amount")}".Trim(),
            _ => Generic(values),
        };
    }

    // One text value says it all; anything else is the arguments as written, on one line.
    private static string Generic(JsonElement values) =>
        values.EnumerateObject().ToList() switch
        {
            [] => string.Empty,
            [{ Value.ValueKind: JsonValueKind.String } only] => OneLine(only.Value.GetString() ?? string.Empty),
            _ => OneLine(values.GetRawText()),
        };

    private static bool CoversAll(JsonElement values, string summary) =>
        values.ValueKind is not JsonValueKind.Object
        || values.EnumerateObject().Count() switch
        {
            0 => true,
            1 => summary.Length > 0,
            _ => false,
        };

    private static string Text(JsonElement values, string name) =>
        values.TryGetProperty(name, out var value)
            ? value.ValueKind is JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText()
            : string.Empty;

    private static string OneLine(string text) => WhiteSpace().Replace(text, " ").Trim();

    private static string Pretty(JsonElement value) => JsonSerializer.Serialize(value, indented);

    private static int? ExitCodeIn(string result) =>
        ExitCode().Match(result) is { Success: true } found
        && int.TryParse(found.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex("\"exit_code\"\\s*:\\s*(-?\\d+)")]
    private static partial Regex ExitCode();

    #endregion

    #region Nested Types

    /// <summary>A tool in words: while it works, once it has, and what it asks to do.</summary>
    private sealed record ToolWords(string Doing, string Done, string Ask);

    #endregion
}
