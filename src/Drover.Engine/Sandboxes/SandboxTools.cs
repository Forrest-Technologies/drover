namespace Drover.Engine.Sandboxes;

/// <summary>
/// The four sandbox tools a bot can be granted. They are registered only on a server with a sandbox provider, so a
/// server without one has no catalog rows for them, and no new bot is granted them: a person decides. Each is offered
/// to a model only when its bot's sandbox policy is not none, and every call goes through the tool gate like any other.
/// </summary>
public static class SandboxTools
{
    #region Private Fields

    private const string EnvironmentProperty =
        """
        "environment": { "type": "string", "description": "The handle of another bot whose sandbox to use, when this bot shares it. Leave it out to use this bot's own sandbox." }
        """;

    private static readonly JsonSerializerOptions resultJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private const string NewSandboxNote = "This is a new sandbox: the one this bot used before was lost at the provider, and its files with it.";

    #endregion

    #region Properties

    public static IReadOnlyList<string> Names { get; } = ["sandbox.run", "sandbox.read_file", "sandbox.write_file", "sandbox.list_files"];

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers the broker, the four sandbox tools, and the computer tools. Needs an <see cref="ISandboxProvider"/>.
    /// The person's controls (<see cref="SandboxControlService"/>) are registered on every server, and use the broker
    /// when there is one.
    /// </summary>
    public static IServiceCollection AddSandboxTools(this IServiceCollection services)
    {
        services.AddSingleton<SandboxBroker>();
        services.AddSingleton(provider => RunCommand(provider.GetRequiredService<SandboxBroker>()));
        services.AddSingleton(provider => ReadFile(provider.GetRequiredService<SandboxBroker>()));
        services.AddSingleton(provider => WriteFile(provider.GetRequiredService<SandboxBroker>()));
        services.AddSingleton(provider => ListFiles(provider.GetRequiredService<SandboxBroker>()));
        return services.AddComputerTools();
    }

    public static bool IsSandboxTool(string name) => Names.Contains(name, StringComparer.Ordinal);

    public static BuiltinTool RunCommand(SandboxBroker broker) =>
        new(
            "sandbox.run",
            "Run a command",
            "Runs a bash command in this bot's sandbox, an isolated computer with its own files, and returns its exit code and output. The output also shows live while it runs.",
            Schema(
                """
                "command": { "type": "string", "description": "The bash command line to run." },
                "cwd": { "type": "string", "description": "The folder to run it in. Leave it out for the home folder." },
                "timeout_seconds": { "type": "integer", "minimum": 1, "description": "Stop the command after this many seconds. The server has its own limit." },
                """,
                "command"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var arguments = call.Arguments;
                var result = await broker.Run(
                    call,
                    Optional(arguments, "environment"),
                    Required(arguments, "command"),
                    Optional(arguments, "cwd"),
                    Seconds(arguments, "timeout_seconds"),
                    cancellationToken);
                return Result(new
                {
                    exit_code = result.ExitCode,
                    stdout = result.Stdout,
                    stderr = result.Stderr,
                    truncated = result.Truncated,
                    error = result.TimedOut ? "The command did not finish within its time limit and was stopped." : null,
                    note = result.NewSandbox ? NewSandboxNote : null,
                });
            });

    public static BuiltinTool ReadFile(SandboxBroker broker) =>
        new(
            "sandbox.read_file",
            "Read a sandbox file",
            "Reads a text file from this bot's sandbox.",
            Schema("""
                "path": { "type": "string", "description": "The file's path in the sandbox." },
                """, "path"),
            ToolSensitivity.Read,
            async (call, cancellationToken) =>
            {
                var path = Required(call.Arguments, "path");
                var result = await broker.ReadFile(call, Optional(call.Arguments, "environment"), path, cancellationToken);
                return Result(new
                {
                    path,
                    content = result.Content,
                    size = result.Size,
                    binary = result.Content is null ? true : (bool?)null,
                    note = result.NewSandbox ? NewSandboxNote : null,
                });
            });

    public static BuiltinTool WriteFile(SandboxBroker broker) =>
        new(
            "sandbox.write_file",
            "Write a sandbox file",
            "Writes a text file in this bot's sandbox, creating its folders and replacing a file already there.",
            Schema("""
                "path": { "type": "string", "description": "The file's path in the sandbox." },
                "content": { "type": "string", "description": "The whole text of the file." },
                """, "path", "content"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var path = Required(call.Arguments, "path");
                var content = Required(call.Arguments, "content", allowEmpty: true);
                var newSandbox = await broker.WriteFile(call, Optional(call.Arguments, "environment"), path, content, cancellationToken);
                return Result(new
                {
                    path,
                    bytes = Encoding.UTF8.GetByteCount(content),
                    note = newSandbox ? NewSandboxNote : null,
                });
            });

    public static BuiltinTool ListFiles(SandboxBroker broker) =>
        new(
            "sandbox.list_files",
            "List sandbox files",
            "Lists the files and folders directly inside a folder of this bot's sandbox.",
            Schema("""
                "path": { "type": "string", "description": "The folder's path in the sandbox." },
                """, "path"),
            ToolSensitivity.Read,
            async (call, cancellationToken) =>
            {
                var path = Required(call.Arguments, "path");
                var result = await broker.ListFiles(call, Optional(call.Arguments, "environment"), path, cancellationToken);
                return Result(new
                {
                    path,
                    entries = result.Entries.Select(entry => new { name = entry.Name, kind = entry.Kind.ToString().ToLowerInvariant(), size = entry.Size }),
                    truncated = result.Truncated,
                    note = result.NewSandbox ? NewSandboxNote : null,
                });
            });

    #endregion

    #region Arguments

    /// <summary>A result as the model reads it. A field with nothing to say is left out.</summary>
    private static JsonElement Result(object value) => JsonSerializer.SerializeToElement(value, resultJson);

    private static JsonElement Schema(string properties, params string[] required) =>
        JsonElement.Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                {{properties.Trim()}}
                {{EnvironmentProperty.Trim()}}
              },
              "required": [{{string.Join(", ", required.Select(name => $"\"{name}\""))}}]
            }
            """);

    private static string Required(JsonElement arguments, string name, bool allowEmpty = false) =>
        Optional(arguments, name, allowEmpty) ?? throw new BuiltinToolException($"The argument {name} is required and must be text.");

    private static string? Optional(JsonElement arguments, string name, bool allowEmpty = false) =>
        arguments.ValueKind is JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && value.GetString() is { } text
        && (allowEmpty || !string.IsNullOrWhiteSpace(text))
            ? text
            : null;

    private static TimeSpan? Seconds(JsonElement arguments, string name) =>
        arguments.ValueKind is JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.Number
        && value.TryGetInt32(out var seconds)
        && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    #endregion
}
