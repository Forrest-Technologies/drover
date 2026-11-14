namespace Drover.Engine.Sandboxes;

/// <summary>
/// The five computer tools: a bot sees its sandbox's screen and drives it with the mouse and keyboard. They are
/// registered with the sandbox tools, only on a server with a sandbox provider, and no new bot is granted them. Each is
/// offered to a model only when its bot's sandbox has a screen, the deployment allows one, and the bot's model can see
/// images or another model is set to read the screen for it; every call goes through the tool gate like any other.
/// Each works in the bot's own sandbox only.
/// </summary>
public static class ComputerTools
{
    #region Private Fields

    private const string NewSandboxNote = "This is a new sandbox: the one this bot used before was replaced, and its files and windows with it.";

    private static readonly JsonSerializerOptions resultJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static readonly string screen = $"The screen is {DesktopCommands.ScreenWidth} by {DesktopCommands.ScreenHeight} pixels; x runs from the left edge, y from the top.";

    #endregion

    #region Properties

    public static IReadOnlyList<string> Names { get; } = ["computer.screenshot", "computer.click", "computer.type", "computer.key", "computer.scroll"];

    #endregion

    #region Public Methods

    /// <summary>Registers the five tools. Needs the <see cref="SandboxBroker"/>.</summary>
    public static IServiceCollection AddComputerTools(this IServiceCollection services)
    {
        services.AddSingleton<ScreenDescriber>();
        services.AddSingleton(provider => Screenshot(provider.GetRequiredService<SandboxBroker>(), provider.GetRequiredService<ScreenDescriber>()));
        services.AddSingleton(provider => Click(provider.GetRequiredService<SandboxBroker>()));
        services.AddSingleton(provider => Type(provider.GetRequiredService<SandboxBroker>()));
        services.AddSingleton(provider => Key(provider.GetRequiredService<SandboxBroker>()));
        services.AddSingleton(provider => Scroll(provider.GetRequiredService<SandboxBroker>()));
        return services;
    }

    public static bool IsComputerTool(string name) => Names.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// The screenshot. Read by the bot's own model, the picture is attached for its next call. With a model set to read
    /// the screen for the bot, that model describes it and the bot gets the description in words: the picture never
    /// reaches the bot's model, and if the reader cannot answer the bot is told so and nothing else sees the picture.
    /// </summary>
    public static BuiltinTool Screenshot(SandboxBroker broker, ScreenDescriber describer) =>
        new(
            "computer.screenshot",
            "Take a screenshot",
            $"Takes a screenshot of this bot's sandbox screen, with the pointer. You see the picture with your next answer, once; take a new one to see the screen again. When another model reads the screen for this bot, you get its description in words instead, with the centre of each thing you can click or type into, and question tells it what to look for. {screen}",
            Schema(
                $$"""
                "question": { "type": "string", "maxLength": {{ScreenDescriber.MaxQuestionCharacters}}, "description": "What to look for, when another model reads the screen for this bot. Ignored when you see the picture yourself." }
                """),
            ToolSensitivity.Read,
            async (call, cancellationToken) =>
            {
                var question = ScreenDescriber.ReadQuestion(call.Arguments);
                var reader = await describer.FindReader(call, cancellationToken);

                if (reader is null && !call.ModelSeesImages)
                {
                    throw new BuiltinToolException("This bot's model cannot read images and no model is set to read the screen for it, so a screenshot cannot be shown. A person can set one in the bot's Sandbox settings.");
                }

                var shot = await broker.Screenshot(call, cancellationToken);

                if (reader is null)
                {
                    call.Attach(new ToolImage(shot.Png, "image/png"));
                    return Result(new { screenshot = "attached", width = shot.Width, height = shot.Height, note = Note(shot.NewSandbox) });
                }

                var description = await describer.Describe(call, reader, shot, question, cancellationToken);
                return Result(new { screenshot = "described", width = shot.Width, height = shot.Height, description, note = Note(shot.NewSandbox) });
            });

    public static BuiltinTool Click(SandboxBroker broker) =>
        new(
            "computer.click",
            "Click on the screen",
            $"Moves the pointer to a point on this bot's sandbox screen and clicks there. {screen}",
            Schema(
                """
                "x": { "type": "integer", "minimum": 0, "maximum": 1023 },
                "y": { "type": "integer", "minimum": 0, "maximum": 767 },
                "button": { "type": "string", "enum": ["left", "right", "middle"], "description": "Left when left out." },
                "double": { "type": "boolean", "description": "Click twice, as for opening a file." }
                """,
                "x",
                "y"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var arguments = call.Arguments;
                var command = DesktopCommands.Click(
                    Integer(arguments, "x") ?? throw Missing("x"),
                    Integer(arguments, "y") ?? throw Missing("y"),
                    Word<MouseButton>(arguments, "button") ?? MouseButton.Left,
                    Flag(arguments, "double"));
                return Done(await broker.Act(call, command, "click there", cancellationToken));
            });

    public static BuiltinTool Type(SandboxBroker broker) =>
        new(
            "computer.type",
            "Type text",
            "Types text on this bot's sandbox screen, into whatever has the keyboard focus. A new line presses Enter.",
            Schema(
                $$"""
                "text": { "type": "string", "minLength": 1, "maxLength": {{DesktopCommands.MaxTypedCharacters}} }
                """,
                "text"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var text = call.Arguments.ValueKind is JsonValueKind.Object
                    && call.Arguments.TryGetProperty("text", out var value)
                    && value.ValueKind is JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : throw Missing("text");
                return Done(await broker.Act(call, DesktopCommands.Type(text), "type the text", cancellationToken));
            });

    public static BuiltinTool Key(SandboxBroker broker) =>
        new(
            "computer.key",
            "Press keys",
            $"Presses keys together on this bot's sandbox screen and lets them go, such as [\"ctrl\", \"l\"] or [\"enter\"]. Keys are letters, digits, and: {string.Join(", ", DesktopCommands.KeyNames.Where(name => name.Length > 1))}.",
            Schema(
                $$"""
                "keys": { "type": "array", "items": { "type": "string" }, "minItems": 1, "maxItems": {{DesktopCommands.MaxKeysAtOnce}} }
                """,
                "keys"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var keys = call.Arguments.ValueKind is JsonValueKind.Object
                    && call.Arguments.TryGetProperty("keys", out var value)
                    && value.ValueKind is JsonValueKind.Array
                    && value.EnumerateArray().All(key => key.ValueKind is JsonValueKind.String)
                        ? value.EnumerateArray().Select(key => key.GetString() ?? string.Empty).ToList()
                        : throw new BuiltinToolException("The argument keys is required and is a list of key names.");
                return Done(await broker.Act(call, DesktopCommands.Key(keys), "press the keys", cancellationToken));
            });

    public static BuiltinTool Scroll(SandboxBroker broker) =>
        new(
            "computer.scroll",
            "Scroll",
            $"Scrolls with the mouse wheel on this bot's sandbox screen, at a point when x and y are given and where the pointer is when not. {screen}",
            Schema(
                $$"""
                "direction": { "type": "string", "enum": ["up", "down"] },
                "amount": { "type": "integer", "minimum": 1, "maximum": {{DesktopCommands.MaxScrollClicks}}, "description": "Wheel clicks. 3 when left out." },
                "x": { "type": "integer", "minimum": 0, "maximum": 1023 },
                "y": { "type": "integer", "minimum": 0, "maximum": 767 }
                """,
                "direction"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var arguments = call.Arguments;
                var command = DesktopCommands.Scroll(
                    Word<ScrollDirection>(arguments, "direction") ?? throw Missing("direction"),
                    Integer(arguments, "amount") ?? 3,
                    Integer(arguments, "x"),
                    Integer(arguments, "y"));
                return Done(await broker.Act(call, command, "scroll", cancellationToken));
            });

    #endregion

    #region Arguments

    private static JsonElement Done(bool newSandbox) => Result(new { done = true, note = Note(newSandbox) });

    private static string? Note(bool newSandbox) => newSandbox ? NewSandboxNote : null;

    private static JsonElement Result(object value) => JsonSerializer.SerializeToElement(value, resultJson);

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

    private static BuiltinToolException Missing(string name) => new($"The argument {name} is required.");

    /// <summary>A whole number, or null when the argument is left out. Any other value is refused.</summary>
    private static int? Integer(JsonElement arguments, string name)
    {
        if (arguments.ValueKind is not JsonValueKind.Object || !arguments.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new BuiltinToolException($"The argument {name} is a whole number.");
    }

    /// <summary>One of an enumeration's names in lower case, or null when the argument is left out.</summary>
    private static T? Word<T>(JsonElement arguments, string name)
        where T : struct, Enum
    {
        if (arguments.ValueKind is not JsonValueKind.Object || !arguments.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.String
            && Enum.GetNames<T>().FirstOrDefault(known => string.Equals(known, value.GetString(), StringComparison.OrdinalIgnoreCase)) is { } found
                ? Enum.Parse<T>(found)
                : throw new BuiltinToolException($"The argument {name} is one of: {string.Join(", ", Enum.GetNames<T>().Select(known => known.ToLowerInvariant()))}.");
    }

    private static bool Flag(JsonElement arguments, string name) =>
        arguments.ValueKind is JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    #endregion
}
