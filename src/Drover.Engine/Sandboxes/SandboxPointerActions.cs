namespace Drover.Engine.Sandboxes;

/// <summary>What a pointer action does to a mouse button, which decides what the server keeps track of.</summary>
internal enum PointerStep
{
    /// <summary>A button goes down and stays down until a release or the time limit.</summary>
    Down,

    /// <summary>The pointer moves. A button that is held stays held.</summary>
    Move,

    /// <summary>A held button is let go of.</summary>
    Up,

    /// <summary>A button goes down, the pointer moves, and the button is let go of, all in one command.</summary>
    Drag,
}


/// <summary>A person's pointer action as the command to run, and what it does to <see cref="Button"/>.</summary>
internal sealed record PointerAction(PointerStep Step, MouseButton Button, CommandRequest Command, string Doing);


/// <summary>Builds the pointer actions of a person's input, checked as a bot's own mouse actions are.</summary>
internal static class SandboxPointerActions
{
    #region Public Methods

    /// <summary>
    /// The pointer action for <c>down</c>, <c>move</c>, <c>up</c> and <c>drag</c>, built by
    /// <see cref="DesktopCommands"/>; null for every other kind of input. A wrong one is refused in a plain sentence.
    /// </summary>
    public static PointerAction? ToPointerAction(this SandboxInputRequest input)
    {
        if (input.Kind is not ("down" or "move" or "up" or "drag"))
        {
            return null;
        }

        var button = ButtonOf(input.Button);

        return input.Kind switch
        {
            "down" => new(
                PointerStep.Down,
                button,
                DesktopCommands.MouseDown(Needed(input.X, "A press"), Needed(input.Y, "A press"), button),
                "press the button there"),
            "move" => new(
                PointerStep.Move,
                button,
                DesktopCommands.MouseMove(Needed(input.X, "A move"), Needed(input.Y, "A move")),
                "move the pointer"),
            "up" => new(
                PointerStep.Up,
                button,
                DesktopCommands.MouseUp(button, input.X, input.Y),
                "let go of the button"),
            _ => new(
                PointerStep.Drag,
                button,
                DesktopCommands.Drag(
                    Needed(input.X, "A drag"),
                    Needed(input.Y, "A drag"),
                    Needed(input.X2, "A drag"),
                    Needed(input.Y2, "A drag"),
                    button),
                "drag there"),
        };
    }

    #endregion

    #region Private Methods

    private const string DragNeeds = "A drag needs x, y, x2 and y2.";

    /// <summary>The number the action needs, or the sentence that says what it needs.</summary>
    private static int Needed(int? value, string action) =>
        value ?? throw new BuiltinToolException(action is "A drag" ? DragNeeds : $"{action} needs x and y.");

    /// <summary>Left, right or middle, left when none is named. Anything else is refused.</summary>
    private static MouseButton ButtonOf(string? name)
    {
        if (name is null)
        {
            return MouseButton.Left;
        }

        var found = Enum.GetNames<MouseButton>()
            .FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

        return found is not null
            ? Enum.Parse<MouseButton>(found)
            : throw new BuiltinToolException($"{name} is not one of: left, right, middle.");
    }

    #endregion
}
