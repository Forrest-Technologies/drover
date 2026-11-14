namespace Drover.Engine.Sandboxes;

/// <summary>A mouse button the computer tools press.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
}


/// <summary>A way the computer tools scroll.</summary>
public enum ScrollDirection
{
    Up,
    Down,
}


/// <summary>
/// The commands behind the computer tools, as a program and its arguments. Nothing a model wrote is ever parsed by a
/// shell: coordinates are checked numbers, key names come from a fixed list and become key symbols, and typed text is
/// one argument of its own. The only shell line here is <see cref="StartDisplay"/>, and Drover wrote all of it. The
/// screen is fixed at 1024 by 768, the size the display is started at.
/// </summary>
public static class DesktopCommands
{
    #region Private Fields

    /// <summary>
    /// Starts the display the way E2B's desktop SDK does, when it is not running: Xvfb on :0 at a fixed size, a wait for
    /// it to answer, the Xfce session, and a wait for its window manager and desktop. Both start in their own session
    /// with no open output, so they outlive this command and its output stream ends when it does. A session just
    /// started gets three more seconds: its processes are up before it has drawn anything (a first screenshot was
    /// black without them, live).
    /// </summary>
    private const string StartDisplayScript =
        """
        export DISPLAY=:0
        if ! xdpyinfo -display :0 >/dev/null 2>&1; then
          setsid -f Xvfb :0 -ac -screen 0 1024x768x24 -retro -dpi 96 -nolisten tcp -nolisten unix </dev/null >/dev/null 2>&1
          for attempt in $(seq 1 40); do xdpyinfo -display :0 >/dev/null 2>&1 && break; sleep 0.25; done
          xdpyinfo -display :0 >/dev/null 2>&1 || exit 3
        fi
        session_running() { pgrep -x xfce4-session >/dev/null && pgrep -x xfwm4 >/dev/null && pgrep -x xfdesktop >/dev/null; }
        session_running && exit 0
        pgrep -x xfce4-session >/dev/null || setsid -f startxfce4 </dev/null >/dev/null 2>&1
        for attempt in $(seq 1 120); do session_running && sleep 3 && exit 0; sleep 0.5; done
        exit 4
        """;

    /// <summary>Key names a model may use, and the X key symbol each one is. Any other name is refused.</summary>
    private static readonly FrozenDictionary<string, string> keySymbols = BuildKeySymbols();

    #endregion

    #region Properties

    public const int ScreenWidth = 1024;

    public const int ScreenHeight = 768;

    public const int MaxTypedCharacters = 2000;

    public const int MaxKeysAtOnce = 5;

    public const int MaxScrollClicks = 20;

    /// <summary>The display every desktop command draws on, also set in the sandbox's environment when it is made.</summary>
    public static IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string> { ["DISPLAY"] = ":0" };

    /// <summary>The key names a model may use, in order, for the tool's description and its refusals.</summary>
    public static IReadOnlyList<string> KeyNames { get; } = [.. keySymbols.Keys.Order(StringComparer.Ordinal)];

    #endregion

    #region Public Methods

    /// <summary>Starts the display and the desktop session when either is not running. Exit code 0 means both run.</summary>
    public static CommandRequest StartDisplay() =>
        CommandRequest.Shell(StartDisplayScript) with { Environment = Environment, Timeout = TimeSpan.FromSeconds(90) };

    /// <summary>A screenshot of the whole screen, with the pointer, into a file only Drover names.</summary>
    public static CommandRequest Screenshot(string path) => Command("scrot", "--pointer", path);

    public static CommandRequest Remove(string path) => Command("rm", "-f", "--", path);

    /// <summary>Moves the pointer and clicks, in one xdotool run, so the click lands where the pointer went.</summary>
    public static CommandRequest Click(int x, int y, MouseButton button, bool twice)
    {
        CheckPoint(x, y);
        string[] repeat = twice ? ["--repeat", "2"] : [];
        return Command(["xdotool", "mousemove", Number(x), Number(y), "click", .. repeat, ButtonNumber(button)]);
    }

    /// <summary>Types the text as it is: it is one argument, after <c>--</c>, so nothing in it is read as an option.</summary>
    public static CommandRequest Type(string text)
    {
        if (text.Length is 0 or > MaxTypedCharacters)
        {
            throw new BuiltinToolException($"The text to type is 1 to {MaxTypedCharacters} characters long.");
        }

        if (text.Any(character => char.IsControl(character) && character is not '\n' and not '\t'))
        {
            throw new BuiltinToolException("The text to type cannot hold control characters other than new lines and tabs. Use computer.key for other keys.");
        }

        return Command("xdotool", "type", "--delay", "12", "--", text);
    }

    /// <summary>Presses the keys together and lets them go, such as ctrl and l. Every name must be on the fixed list.</summary>
    public static CommandRequest Key(IReadOnlyList<string> names)
    {
        if (names.Count is 0 or > MaxKeysAtOnce)
        {
            throw new BuiltinToolException($"Press 1 to {MaxKeysAtOnce} keys at once.");
        }

        List<string> symbols =
        [
            .. names.Select(name => keySymbols.TryGetValue(name.Trim().ToLowerInvariant(), out var symbol)
                ? symbol
                : throw new BuiltinToolException($"There is no key named \"{name}\". Use a letter, a digit, or one of: {string.Join(", ", KeyNames.Where(key => key.Length > 1))}.")),
        ];
        return Command("xdotool", "key", string.Join('+', symbols));
    }

    /// <summary>Scrolls with the wheel, at a point when one is given and where the pointer is when not.</summary>
    public static CommandRequest Scroll(ScrollDirection direction, int amount, int? x, int? y)
    {
        if (amount is < 1 or > MaxScrollClicks)
        {
            throw new BuiltinToolException($"Scroll by 1 to {MaxScrollClicks}.");
        }

        if (x.HasValue != y.HasValue)
        {
            throw new BuiltinToolException("Give both x and y to scroll at a point, or neither to scroll where the pointer is.");
        }

        string[] move = [];

        if (x is { } atX && y is { } atY)
        {
            CheckPoint(atX, atY);
            move = ["mousemove", Number(atX), Number(atY)];
        }

        var wheel = direction is ScrollDirection.Up ? "4" : "5";
        return Command(["xdotool", .. move, "click", "--repeat", Number(amount), wheel]);
    }

    /// <summary>The width and height a PNG file says it has, or null when the bytes are not a PNG.</summary>
    public static (int Width, int Height)? ReadPngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var header = "IHDR"u8;

        if (png.Length < 24 || !png[..8].SequenceEqual(signature) || !png[12..16].SequenceEqual(header))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(png[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    #endregion

    #region Private Methods

    private static CommandRequest Command(params string[] argv) =>
        new() { Program = argv[0], Arguments = argv[1..], Environment = Environment, Timeout = TimeSpan.FromSeconds(30) };

    private static void CheckPoint(int x, int y)
    {
        if (x is < 0 or >= ScreenWidth || y is < 0 or >= ScreenHeight)
        {
            throw new BuiltinToolException($"The point ({x}, {y}) is off the screen. The screen is {ScreenWidth} by {ScreenHeight}: x is 0 to {ScreenWidth - 1}, y is 0 to {ScreenHeight - 1}.");
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string ButtonNumber(MouseButton button) => button switch
    {
        MouseButton.Right => "3",
        MouseButton.Middle => "2",
        _ => "1",
    };

    /// <summary>The names E2B's desktop SDK maps, plus every lower-case letter and digit, which are their own symbols.</summary>
    private static FrozenDictionary<string, string> BuildKeySymbols()
    {
        Dictionary<string, string> named = new(StringComparer.Ordinal)
        {
            ["alt"] = "Alt_L",
            ["alt_left"] = "Alt_L",
            ["alt_right"] = "Alt_R",
            ["backspace"] = "BackSpace",
            ["caps_lock"] = "Caps_Lock",
            ["ctrl"] = "Control_L",
            ["control"] = "Control_L",
            ["ctrl_left"] = "Control_L",
            ["ctrl_right"] = "Control_R",
            ["delete"] = "Delete",
            ["down"] = "Down",
            ["end"] = "End",
            ["enter"] = "Return",
            ["escape"] = "Escape",
            ["esc"] = "Escape",
            ["home"] = "Home",
            ["insert"] = "Insert",
            ["left"] = "Left",
            ["menu"] = "Menu",
            ["page_down"] = "Page_Down",
            ["page_up"] = "Page_Up",
            ["right"] = "Right",
            ["shift"] = "Shift_L",
            ["shift_left"] = "Shift_L",
            ["shift_right"] = "Shift_R",
            ["space"] = "space",
            ["super"] = "Super_L",
            ["tab"] = "Tab",
            ["up"] = "Up",
            ["minus"] = "minus",
            ["equal"] = "equal",
            ["period"] = "period",
            ["comma"] = "comma",
            ["slash"] = "slash",
        };

        foreach (var function in Enumerable.Range(1, 12))
        {
            named[$"f{function}"] = $"F{function}";
        }

        foreach (var character in "abcdefghijklmnopqrstuvwxyz0123456789")
        {
            named[character.ToString()] = character.ToString();
        }

        return named.ToFrozenDictionary(StringComparer.Ordinal);
    }

    #endregion
}
