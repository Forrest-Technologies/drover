namespace Drover.Tests.Common;

/// <summary>What a sandbox's pointer was told to do, in the order a person's input reached it.</summary>
public enum PointerKind
{
    Down,
    Move,
    Up,
}

/// <summary>One pointer event in a sandbox: the button it names (a number, as the desktop tool does) and where it was.</summary>
public sealed record PointerEvent(PointerKind Kind, int X, int Y, string? Button);

/// <summary>
/// Reads the pointer events out of the commands a recording sandbox ran, so a test says what the sandbox was told in
/// the words of a pointer (down, move, up) and not the arguments of the tool that did it.
/// </summary>
public static class PointerEvents
{
    #region Public Methods

    /// <summary>The pointer events the commands carry, in order. A click's own move and press are left to the click.</summary>
    public static List<PointerEvent> Of(IEnumerable<CommandRequest> commands) =>
        [.. commands.Where(command => command.Program is "xdotool").SelectMany(Events)];

    /// <summary>The buttons still held after the events, each as it was named when it went down.</summary>
    public static List<string> Held(IEnumerable<PointerEvent> events)
    {
        List<string> held = [];

        foreach (var pointer in events)
        {
            if (pointer.Kind is PointerKind.Down)
            {
                held.Add(pointer.Button!);
            }
            else if (pointer.Kind is PointerKind.Up)
            {
                held.Remove(pointer.Button!);
            }
        }

        return held;
    }

    #endregion

    #region Private Methods

    private static IEnumerable<PointerEvent> Events(CommandRequest command)
    {
        var arguments = command.Arguments;
        var (x, y) = (0, 0);

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "mousemove":
                    (x, y) = (int.Parse(arguments[index + 1], CultureInfo.InvariantCulture), int.Parse(arguments[index + 2], CultureInfo.InvariantCulture));

                    if (!(index + 3 < arguments.Count && arguments[index + 3] is "mousedown" or "mouseup" or "click"))
                    {
                        yield return new PointerEvent(PointerKind.Move, x, y, null);
                    }

                    break;
                case "mousedown":
                    yield return new PointerEvent(PointerKind.Down, x, y, arguments[index + 1]);
                    break;
                case "mouseup":
                    yield return new PointerEvent(PointerKind.Up, x, y, arguments[index + 1]);
                    break;
            }
        }
    }

    #endregion
}
