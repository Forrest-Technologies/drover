namespace Drover.Ui.Components;

/// <summary>
/// A bot's character: a head with a visor, a top, a mark, and a tilt, drawn on a 64 unit grid. The same seed
/// always gives the same character. The part sets are the ones the owner approved on issue 26.
/// </summary>
public sealed record BotLook(string Head, string Visor, string Top, string Mark, int Tilt)
{
    #region Private Fields

    private static readonly string[] heads =
    [
        "M12 14L52 14L57 30L44 55L20 55L7 30Z",
        "M16 11L48 11L55 18L55 48L48 55L16 55L9 48L9 18Z",
        "M9 36C9 20 19 9 32 9C45 9 55 20 55 36L55 48L48 55L16 55L9 48Z",
        "M32 7L55 20L55 44L32 57L9 44L9 20Z",
        "M9 13L55 13L55 33C55 46 45 54 32 58C19 54 9 46 9 33Z",
        "M21 7L43 7L50 15L50 46L42 58L22 58L14 46L14 15Z",
    ];

    private static readonly string[] visors =
    [
        """<rect x="4" y="27" width="56" height="6" rx="3"/>""",
        """<rect x="4" y="22" width="56" height="13" rx="4"/>""",
        """<rect x="4" y="23" width="26.5" height="11" rx="2"/><rect x="33.5" y="23" width="26.5" height="11" rx="2"/>""",
        """<circle cx="32" cy="29" r="9.5"/>""",
        """<path d="M4 21L60 28L60 37L4 30Z"/>""",
        """<rect x="4" y="35" width="56" height="8" rx="2"/>""",
    ];

    private static readonly string[] tops =
    [
        string.Empty,
        """<path class="dv-character-line" d="M41 14L46 3"/><circle class="dv-character-dot" cx="46.5" cy="3.5" r="3"/>""",
        """<path d="M11 20L8 3L24 12Z"/><path d="M53 20L56 3L40 12Z"/>""",
        """<path d="M13 22C4 16 4 8 9 2C10 9 15 12 22 13Z"/><path d="M51 22C60 16 60 8 55 2C54 9 49 12 42 13Z"/>""",
        """<path d="M22 14L36 1L44 14Z"/>""",
        """<path d="M12 26L2 21L2 36L12 40Z"/><path d="M52 26L62 21L62 36L52 40Z"/>""",
    ];

    private static readonly string[] marks =
    [
        string.Empty,
        """<rect x="29.5" y="0" width="5" height="19"/>""",
        """<rect x="38" y="43" width="9" height="5" rx="1"/>""",
        """<path class="dv-character-line" d="M24 43L32 48L40 43"/>""",
        """<circle cx="19" cy="45" r="1.8"/><circle cx="24.5" cy="45" r="1.8"/><circle cx="30" cy="45" r="1.8"/>""",
        """<path d="M24 60L32 48L40 60Z"/>""",
    ];

    private static readonly int[] tilts = [-7, 0, 0, 7];

    #endregion

    #region Public Methods

    /// <summary>The character a seed gives. Each part is picked from its own bits of the seed.</summary>
    public static BotLook From(uint seed)
    {
        var mixed = Mix(seed);
        return new(
            heads[mixed % 6],
            visors[mixed / 6 % 6],
            tops[mixed / 36 % 6],
            marks[mixed / 216 % 6],
            tilts[mixed / 1296 % 4]);
    }

    /// <summary>A seed from a bot's name and hue, for callers that have nothing better.</summary>
    public static uint SeedFrom(string name, int hue)
    {
        var hash = 2166136261;
        foreach (var letter in name)
        {
            hash = (hash ^ letter) * 16777619;
        }

        return hash ^ (uint)hue;
    }

    #endregion

    #region Private Methods

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352D;
        value ^= value >> 15;
        value *= 0x846CA68B;
        return value ^ (value >> 16);
    }

    #endregion
}
