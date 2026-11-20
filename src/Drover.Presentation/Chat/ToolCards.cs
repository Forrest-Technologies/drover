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
        ("sandbox.share_file", new("Sharing a file", "Shared a file", "share a file")),
        ("computer.screenshot", new("Taking a screenshot", "Took a screenshot", "take a screenshot")),
        ("computer.click", new("Clicking", "Clicked", "click")),
        ("computer.type", new("Typing", "Typed", "type")),
        ("computer.key", new("Pressing keys", "Pressed keys", "press keys")),
        ("computer.scroll", new("Scrolling", "Scrolled", "scroll")),
        ("computer.handover", new("Asking for you at the screen", "Asked for you at the screen", "have you take over the screen")),
        ("crew.list", new("Looking over its crew", "Looked over its crew", "look over its crew")),
        ("crew.add", new("Making a bot", "Made a bot", "make a bot")),
        ("crew.change", new("Changing a bot", "Changed a bot", "change a bot")),
        ("crew.remove", new("Removing a bot", "Removed a bot", "remove a bot")),
        ("time.now", new("Checking the time", "Checked the time", "check the time")),
        ("memory.save", new("Saving a note", "Saved a note", "save a note")),
        ("memory.change", new("Changing a note", "Changed a note", "change a note")),
        ("memory.forget", new("Removing a note", "Removed a note", "remove a note")),
        ("memory.list", new("Reading its notes", "Read its notes", "read its notes")),
        ("delegate", new("Handing work over", "Handed work over", "hand work to a bot beneath it")));

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
        var toolName = Canonical(call.ToolName);
        var summary = Summarize(toolName, arguments);

        // A stored result is JSON. The preview in the call's events is the start of it, which may be cut off.
        var resultText = result?.Result is { Length: > 0 } stored ? Pretty(JsonElement.Parse(stored)) : call.Preview;

        var command = toolName == "sandbox.run" && ParsedOrNull(resultText) is { ValueKind: JsonValueKind.Object } parsed
            ? parsed
            : (JsonElement?)null;
        var exitCode = ExitCodeIn(resultText);
        var commandError = command is { } finished && finished.TryGetProperty("error", out var error)
            && error.ValueKind is JsonValueKind.String ? error.GetString() : null;

        if (phase is ToolPhase.Done && toolName == "sandbox.run"
            && (exitCode is not (null or 0) || !string.IsNullOrWhiteSpace(commandError)))
        {
            phase = ToolPhase.Failed;
        }

        // The result is shown decoded: as JSON, the tabs and line breaks inside its strings would read as escapes.
        var shown = ToolResults.Shown(toolName, resultText, arguments);
        var card = new ToolCard(
            call.ToolCallId,
            phase,
            LabelFor(words, phase),
            words.Ask,
            summary,
            Pretty(arguments),
            CoversAll(arguments, summary),
            shown?.Text,
            exitCode,
            call.Pieces.Count == 0 && toolName == "sandbox.run" ? ToolResults.CommandOutput(resultText) : call.Pieces)
        {
            ResultLanguage = shown?.Language,
            ResultLength = resultText.Length,
            CommandError = commandError,
            Facts = FactsOf(toolName, arguments, call.Details),
            Takeover = TakeoverOf(call, arguments, resultText),
        };

        return phase is ToolPhase.Done ? card.AsNote(toolName, arguments, resultText) : card;
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

    #region Takeover

    /// <summary>
    /// A request for a person at the screen, while it waits and once the person answered it. Anything else that ended
    /// it (the run was stopped, a restart cut it off) is told by the plain tool card, which says so in its own words.
    /// </summary>
    private static Takeover? TakeoverOf(ToolCallView call, JsonElement arguments, string result)
    {
        if (Canonical(call.ToolName) is not "computer.handover")
        {
            return null;
        }

        TakeoverPhase? phase = call.State switch
        {
            "requested" or "awaiting_approval" => TakeoverPhase.Asked,
            "approved" or "running" => TakeoverPhase.Done,
            "completed" when IsDone(result) => TakeoverPhase.Done,
            // Only the person's answer denies it; its result arrives after the answer's event, which says so already.
            "denied" when result.Length == 0 || IsCouldNot(result) => TakeoverPhase.CouldNot,
            _ => null,
        };
        var reason = arguments.ValueKind is JsonValueKind.Object ? Text(arguments, "reason") : string.Empty;
        return phase is { } known ? new Takeover(call.ToolCallId, reason, known) : null;
    }

    // The person's "I could not do it" is the call's result, whole or as the start of it; any other refusal is an error.
    private static bool IsCouldNot(string result) => NotDone().IsMatch(result);

    // "I'm done" lets the call run, and its result says so. A result that does not is a refusal or a failure.
    private static bool IsDone(string result) => DoneResult().IsMatch(result);

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

    #region Notes

    // A bot's note, once kept, changed, or removed, reads as the note itself, so nothing has to be opened to know
    // what the bot remembers. The words come from the result, which holds the note as it was kept.
    private static ToolCard AsNote(this ToolCard card, string toolName, JsonElement arguments, string result)
    {
        if (!toolName.StartsWith("memory.", StringComparison.Ordinal))
        {
            return card;
        }

        var kept = ParsedOrNull(result) is { ValueKind: JsonValueKind.Object } whole ? whole : (JsonElement?)null;
        var note = kept is { } found && found.TryGetProperty("note", out var saved) && saved.ValueKind is JsonValueKind.Object ? saved : (JsonElement?)null;
        var text = (note is { } written ? Text(written, "text") : string.Empty) is { Length: > 0 } stored ? stored : Text(arguments, "text");

        return toolName switch
        {
            "memory.save" when text.Length > 0 => card with
            {
                Label = MemoryWords.Remembered(text),
                Summary = string.Empty,
                NoteId = note is { } keptNote && Guid.TryParse(Text(keptNote, "id"), out var noteId) ? noteId : null,
                NoteText = text,
            },
            "memory.change" when text.Length > 0 => card with
            {
                Label = MemoryWords.Updated,
                Summary = string.Empty,
                Facts = [.. new ToolFact[] { new("Before", kept is { } before ? Text(before, "before") : string.Empty), new("After", text) }
                    .Where(fact => fact.Value.Length > 0)],
            },
            "memory.forget" when kept is { } forgot && Text(forgot, "text") is { Length: > 0 } forgotten => card with
            {
                Label = MemoryWords.Forgot(forgotten),
                Summary = string.Empty,
            },
            _ => card,
        };
    }

    // A result's start, as a call's events carry it, can be cut off. Read as a block with more to come, a cut-off
    // result is simply not a whole value yet. A result that is not a JSON object, such as plain text, is no note.
    private static JsonElement? ParsedOrNull(string result)
    {
        if (!result.TrimStart().StartsWith('{'))
        {
            return null;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(result), isFinalBlock: false, state: default);
        return JsonElement.TryParseValue(ref reader, out var parsed) ? parsed : null;
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
            "sandbox.read_file" or "sandbox.write_file" or "sandbox.list_files" or "sandbox.share_file" => Text(values, "path"),
            "computer.screenshot" => string.Empty,
            "computer.click" => $"{Text(values, "x")}, {Text(values, "y")}",
            "computer.type" => OneLine(Text(values, "text")),
            "computer.key" => values.TryGetProperty("keys", out var keys) && keys.ValueKind is JsonValueKind.Array
                ? string.Join("+", keys.EnumerateArray().Select(key => key.ToString()))
                : string.Empty,
            "computer.scroll" => $"{Text(values, "direction")} {Text(values, "amount")}".Trim(),
            "crew.add" => NameAndPurpose(values),
            _ => Generic(values),
        };
    }

    // Every detail of a bot a bot asks to make or remove, each under its own name, so one look decides it.
    // What the arguments do not hold (the model a new bot will use, the bot it goes beneath) the server adds.
    private static IReadOnlyList<ToolFact> FactsOf(string toolName, JsonElement values, IReadOnlyList<ToolApprovalDetail>? details)
    {
        string Detail(string name) => details?.FirstOrDefault(detail => detail.Name == name)?.Text ?? string.Empty;

        if (values.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        (string Name, string Value)[] facts = toolName switch
        {
            "crew.add" =>
            [
                ("Name", Text(values, "name")),
                ("What it is for", Text(values, "description")),
                ("Instructions", Text(values, "instructions")),
                ("Model", Detail(ToolApprovalRequested.ModelDetail)),
                ("Works beneath", Detail(ToolApprovalRequested.BeneathDetail)),
            ],
            "crew.remove" =>
            [
                ("The bots beneath it", Text(values, "beneath") switch
                {
                    "remove" => "Removed too",
                    "keep" => "Kept, and moved up",
                    _ => string.Empty,
                }),
            ],
            _ => [],
        };

        return [.. facts.Where(fact => fact.Value.Trim().Length > 0).Select(fact => new ToolFact(fact.Name, fact.Value.Trim()))];
    }

    // The new bot as a person would say it: its name, then what it is for.
    private static string NameAndPurpose(JsonElement values) =>
        (Text(values, "name"), OneLine(Text(values, "description"))) switch
        {
            (var name, "") => name,
            ("", var purpose) => purpose,
            (var name, var purpose) => $"{name}: {purpose}",
        };

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

    [GeneratedRegex("^\\s*\\{\\s*\"done\"\\s*:\\s*false\\b")]
    private static partial Regex NotDone();

    [GeneratedRegex("^\\s*\\{\\s*\"done\"\\s*:\\s*true\\b")]
    private static partial Regex DoneResult();

    #endregion

    #region Nested Types

    /// <summary>A tool in words: while it works, once it has, and what it asks to do.</summary>
    private sealed record ToolWords(string Doing, string Done, string Ask);

    #endregion
}
