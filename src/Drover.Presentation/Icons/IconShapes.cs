namespace Drover.Ui.Icons;

/// <summary>
/// The shape of each icon on a 24 unit grid, drawn with a stroke and no fill. The set is Drover's own: straight
/// segments, mitered joins, and cut corners where a shape closes, so an icon is cut the way a bot's head and a
/// faceted frame are.
/// </summary>
public static class IconShapes
{
    #region Private Fields

    private static readonly Dictionary<IconName, string> shapes = new()
    {
        [IconName.Account] =
            """<path d="M9 3h6l2 2v4l-2 2H9L7 9V5z"/><path d="M4 21v-4l3-3h10l3 3v4z"/>""",
        [IconName.ArrowLeft] =
            """<path d="M11 5 4 12l7 7"/><path d="M5 12h15"/>""",
        [IconName.Attach] =
            """<path d="M17 8v8l-5 5-5-5V6l3-3h2l2 2v10l-2 2-2-2V8"/>""",
        [IconName.Check] =
            """<path d="m4 12 5 5L20 6"/>""",
        [IconName.ChevronDown] =
            """<path d="m6 9 6 6 6-6"/>""",
        [IconName.ChevronLeft] =
            """<path d="m15 6-6 6 6 6"/>""",
        [IconName.ChevronRight] =
            """<path d="m9 6 6 6-6 6"/>""",
        [IconName.Close] =
            """<path d="m6 6 12 12"/><path d="M18 6 6 18"/>""",
        [IconName.Command] =
            """<path d="m4 7 5 5-5 5"/><path d="M12 18h8"/>""",
        [IconName.Conversation] =
            """<path d="M6 4h14v12l-3 3h-6l-5 3v-3H3V7z"/><path d="M7 9h9m-9 4h7"/>""",
        // One bubble in front of another: talk beside the main conversation.
        [IconName.SideChats] =
            """<path d="M3 10h12v8l-2 2H8l-4 3v-3H3z"/><path d="M8 10V5h13v9l-2 2h-4"/>""",
        [IconName.Copy] =
            """<path d="M11 8h10v10l-3 3H8V11z"/><path d="M5 16H3V6l3-3h10v2"/>""",
        [IconName.Crew] =
            """<path d="M7 5h10l3 4v8l-3 3H7l-3-3V9z"/><path d="M7 10h10v4H7zM12 5V2"/>""",
        [IconName.Dot] =
            """<path d="M10 10h4v4h-4z"/>""",
        [IconName.Download] =
            """<path d="M12 4v11"/><path d="m7 10 5 5 5-5"/><path d="M5 20h14"/>""",
        [IconName.Edit] =
            """<path d="m4 20 1-5L16 4l4 4L9 19z"/><path d="m14 6 4 4"/>""",
        [IconName.Expand] =
            """<path d="M4 9V4h5"/><path d="M15 4h5v5"/><path d="M20 15v5h-5"/><path d="M9 20H4v-5"/>""",
        [IconName.ExternalLink] =
            """<path d="M14 4h6v6"/><path d="m20 4-9 9"/><path d="M18 14v4l-2 2H4V9l3-3h3"/>""",
        [IconName.File] =
            """<path d="M6 3h8l5 5v13H6z"/><path d="M14 3v5h5"/>""",
        [IconName.Info] =
            """<path d="M8 3h8l5 5v8l-5 5H8l-5-5V8z"/><path d="M12 11v6"/><path d="M12 7.5h.01"/>""",
        [IconName.Memory] =
            """<path d="M6 3h12v18l-6-4-6 4z"/>""",
        [IconName.Microphone] =
            """<path d="M10 3h4l1 1v8l-1 1h-4l-1-1V4z"/><path d="M6 11v2l3 3h6l3-3v-2"/><path d="M12 16v5"/><path d="M8 21h8"/>""",
        [IconName.Model] =
            """<path d="M9 7h8v10H7V9z"/><path d="M10 3v4M14 3v4M10 17v4M14 17v4M3 10h4M3 14h4M17 10h4M17 14h4"/>""",
        [IconName.More] =
            """<path d="M4.5 11.5h1v1h-1z"/><path d="M11.5 11.5h1v1h-1z"/><path d="M18.5 11.5h1v1h-1z"/>""",
        [IconName.Pause] =
            """<path d="M7 4h3v16H7z"/><path d="M14 4h3v16h-3z"/>""",
        [IconName.Play] =
            """<path d="m7 4 12 8-12 8z"/>""",
        [IconName.Plus] =
            """<path d="M5 12h14"/><path d="M12 5v14"/>""",
        [IconName.Project] =
            """<path d="M3 7l3-3h4l2 2h9v11l-3 3H3z"/>""",
        [IconName.Regenerate] =
            """<path d="M20 13v2l-4 4H8l-4-4V9l4-4h8l3 3"/><path d="M19 3v5h-5"/>""",
        [IconName.Remove] =
            """<path d="M4 6h16"/><path d="m6 6 1 15h10l1-15"/><path d="M9 6V3h6v3"/><path d="M10 11v6"/><path d="M14 11v6"/>""",
        [IconName.Screen] =
            """<path d="M3 4h18v12H3z"/><path d="M8 20h8"/><path d="M12 16v4"/>""",
        [IconName.Search] =
            """<path d="M8 4h6l4 4v6l-4 4H8l-4-4V8z"/><path d="m17 17 4 4"/>""",
        [IconName.Send] =
            """<path d="m3 11 17-7-7 16-2-7z"/><path d="m11 13 9-9"/>""",
        [IconName.Settings] =
            """<path d="M4 8h9"/><path d="M17 8h3"/><path d="M13 6h4v4h-4z"/><path d="M4 16h3"/><path d="M11 16h9"/><path d="M7 14h4v4H7z"/>""",
        [IconName.Share] =
            """<path d="M12 3v12"/><path d="m8 7 4-4 4 4"/><path d="M5 11v10h14V11"/>""",
        [IconName.Speaker] =
            """<path d="M4 9h4l5-4v14l-5-4H4z"/><path d="M17 9v6"/><path d="M20 6v12"/>""",
        [IconName.Stop] =
            """<path d="M8 5h11v11l-3 3H5V8z"/>""",
        [IconName.Thought] =
            """<path d="M5 4h14l2 2v8l-2 2h-7l-5 4v-4H5l-2-2V6z"/><path d="M8 10h1"/><path d="M12 10h1"/><path d="M16 10h1"/>""",
        [IconName.Tool] =
            """<path d="M14 3h4l3 3v4l-3 3h-4l-3-3V6z"/><path d="M12 12 4 20"/>""",
        [IconName.Warning] =
            """<path d="M12 3 22 20H2z"/><path d="M12 10v4"/><path d="M12 17h.01"/>""",
        [IconName.Wrap] =
            """<path d="M4 6h16"/><path d="M4 12h13l3 3-3 3h-4"/><path d="m15 15-3 3 3 3"/><path d="M4 18h4"/>""",
    };

    #endregion

    #region Public Methods

    public static string For(IconName name) => shapes[name];

    #endregion
}
