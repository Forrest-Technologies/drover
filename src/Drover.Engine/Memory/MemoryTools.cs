namespace Drover.Engine.Memory;

/// <summary>
/// The four memory tools: a bot saves, changes, removes, and lists short notes about the person it works for. They
/// are on every server, so each is a catalog row, off until granted and narrowed by every grant above like any other
/// tool, and every call passes the tool gate. They are offered to the model only while memory is on for the bot. What
/// a call may touch is decided by <see cref="MemoryService"/>, inside one short transaction as the run's person: the
/// calling bot's own notes about that person, and nothing else.
/// </summary>
public static class MemoryTools
{
    #region Private Fields

    private const string NoteIdProperty =
        """
        "note_id": { "type": "string", "format": "uuid", "description": "The id of one of your notes, as memory_list gives it." }
        """;

    private const string TextProperty =
        """
        "text": { "type": "string", "description": "The note, in your own words: one fact or preference about the person, in a sentence or two." }
        """;

    #endregion

    #region Properties

    public static IReadOnlyList<string> Names { get; } = ["memory.save", "memory.change", "memory.forget", "memory.list"];

    #endregion

    #region Public Methods

    public static IServiceCollection AddMemoryTools(this IServiceCollection services)
    {
        services.AddScoped<MemoryService>();
        services.AddSingleton(_ => SaveNote());
        services.AddSingleton(_ => ChangeNote());
        services.AddSingleton(_ => ForgetNote());
        return services.AddSingleton(_ => ListNotes());
    }

    public static bool IsMemoryTool(string name) => Names.Contains(name, StringComparer.Ordinal);

    public static BuiltinTool SaveNote() =>
        new(
            "memory.save",
            "Save a note",
            "Saves a short note about the person you work for, so you have it in every later conversation with them. One fact or preference to a note, in your own words. When a note you already have changes, change it with memory_change instead of saving another. Never save a password, key, or other secret.",
            Schema(TextProperty, "text"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var text = Text(call);
                var (note, used, limits) = await call.Tenant.Transact(
                    services => services.GetRequiredService<MemoryService>().Save(Writer(call), text, cancellationToken),
                    cancellationToken);
                return Result(new { note = Describe(note), characters_used = used, characters_allowed = limits.NotesCharacters });
            });

    public static BuiltinTool ChangeNote() =>
        new(
            "memory.change",
            "Change a note",
            "Rewrites one of your notes about the person you work for, when what it says has changed or to merge two notes into one. The note is replaced by the new text.",
            Schema($"{NoteIdProperty.Trim()},\n{TextProperty.Trim()}", "note_id", "text"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var noteId = NoteId(call.Arguments);
                var text = Text(call);
                var (note, used, limits) = await call.Tenant.Transact(
                    services => services.GetRequiredService<MemoryService>().Change(Writer(call), noteId, text, cancellationToken),
                    cancellationToken);
                return Result(new { note = Describe(note), characters_used = used, characters_allowed = limits.NotesCharacters });
            });

    public static BuiltinTool ForgetNote() =>
        new(
            "memory.forget",
            "Remove a note",
            "Removes one of your notes about the person you work for, when it no longer holds or the person asks you to forget it.",
            Schema(NoteIdProperty, "note_id"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var noteId = NoteId(call.Arguments);
                var note = await call.Tenant.Transact(
                    services => services.GetRequiredService<MemoryService>().Forget(Writer(call), noteId, cancellationToken),
                    cancellationToken);
                return Result(new { removed = note.Id });
            });

    public static BuiltinTool ListNotes() =>
        new(
            "memory.list",
            "List notes",
            "Lists every note you keep about the person you work for, oldest first, with each one's id, and how many characters they use of what they may hold.",
            JsonElement.Parse("""{ "type": "object", "properties": {} }"""),
            ToolSensitivity.Read,
            async (call, cancellationToken) =>
            {
                var (notes, limits) = await call.Tenant.Transact(
                    services => services.GetRequiredService<MemoryService>().List(Writer(call), cancellationToken),
                    cancellationToken);
                return Result(new
                {
                    notes = notes.Select(Describe),
                    characters_used = notes.Sum(note => note.Text.Length),
                    characters_allowed = limits.NotesCharacters,
                    longest_note = limits.NoteCharacters,
                });
            });

    #endregion

    #region Arguments

    private static NoteWriter Writer(BuiltinToolCall call) =>
        new(call.BotId, call.Tenant.UserId, call.ThreadId, call.RunId, call.ToolCallId);

    private static object Describe(MemoryNote note) => new
    {
        id = note.Id,
        text = note.Text,
        written_by = note.WrittenByBotId is null ? "person" : "bot",
        changed_at = note.UpdatedAt,
    };

    private static JsonElement Result(object value) => JsonSerializer.SerializeToElement(value);

    private static JsonElement Schema(string properties, params string[] required) =>
        JsonElement.Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                {{properties.Trim()}}
              },
              "required": [{{string.Join(", ", required.Select(name => $"\"{name}\""))}}]
            }
            """);

    /// <summary>The note's text as it is kept: trimmed, and with anything the run knows to be secret taken out.</summary>
    private static string Text(BuiltinToolCall call) =>
        call.Arguments.ValueKind is JsonValueKind.Object
        && call.Arguments.TryGetProperty("text", out var value)
        && value.ValueKind is JsonValueKind.String
            ? call.Redact(value.GetString() ?? string.Empty).Trim()
            : throw new BuiltinToolException("The argument text is required: the note, in a sentence or two.");

    private static Guid NoteId(JsonElement arguments) =>
        arguments.ValueKind is JsonValueKind.Object
        && arguments.TryGetProperty("note_id", out var value)
        && value.ValueKind is JsonValueKind.String
        && Guid.TryParse(value.GetString()?.Trim(), out var id)
            ? id
            : throw new BuiltinToolException("The argument note_id is required and must be a note's id, as memory_list gives it.");

    #endregion
}
