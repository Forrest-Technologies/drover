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
                DesktopCommands.MouseDown(input.X ?? throw Needs("a press", "x and y"), input.Y ?? throw Needs("a press", "x and y"), button),
                "press the button there"),
            "move" => new(
                PointerStep.Move,
                button,
                DesktopCommands.MouseMove(input.X ?? throw Needs("a move", "x and y"), input.Y ?? throw Needs("a move", "x and y")),
                "move the pointer"),
            "up" => new(PointerStep.Up, button, DesktopCommands.MouseUp(button, input.X, input.Y), "let go of the button"),
            "drag" => new(
                PointerStep.Drag,
                button,
                DesktopCommands.Drag(
                    input.X ?? throw Needs("a drag", "x, y, x2 and y2"),
                    input.Y ?? throw Needs("a drag", "x, y, x2 and y2"),
                    input.X2 ?? throw Needs("a drag", "x, y, x2 and y2"),
                    input.Y2 ?? throw Needs("a drag", "x, y, x2 and y2"),
                    button),
                "drag there"),
            _ => null,
        };
    }

    #endregion

    #region Private Methods

    private static BuiltinToolException Needs(string action, string values) => new($"{action} needs {values}.");

    /// <summary>Left, right or middle, left when none is named. Anything else is refused.</summary>
    private static MouseButton ButtonOf(string? name)
    {
        if (name is null)
        {
            return MouseButton.Left;
        }

        return Enum.GetNames<MouseButton>().FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) is { } found
            ? Enum.Parse<MouseButton>(found)
            : throw new BuiltinToolException($"{name} is not one of: left, right, middle.");
    }

    #endregion
}
