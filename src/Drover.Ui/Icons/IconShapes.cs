namespace Drover.Ui.Icons;

/// <summary>
/// The shape of each icon on a 24 unit grid, drawn with a stroke and no fill.
/// Shapes come from the Lucide icon set (ISC license, see LICENSE-icons.txt).
/// </summary>
public static class IconShapes
{
    #region Private Fields

    private static readonly Dictionary<IconName, string> shapes = new()
    {
        [IconName.ArrowLeft] =
            """<path d="m12 19-7-7 7-7"/><path d="M19 12H5"/>""",
        [IconName.Attach] =
            """<path d="M13.234 20.252 21 12.3"/><path d="m16 6-8.414 8.586a2 2 0 0 0 0 2.828 2 2 0 0 0 2.828 0l8.414-8.586a4 4 0 0 0 0-5.656 4 4 0 0 0-5.656 0l-8.415 8.585a6 6 0 1 0 8.486 8.486"/>""",
        [IconName.Check] =
            """<path d="M20 6 9 17l-5-5"/>""",
        [IconName.ChevronDown] =
            """<path d="m6 9 6 6 6-6"/>""",
        [IconName.ChevronRight] =
            """<path d="m9 18 6-6-6-6"/>""",
        [IconName.Close] =
            """<path d="M18 6 6 18"/><path d="m6 6 12 12"/>""",
        [IconName.Copy] =
            """<rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>""",
        [IconName.Edit] =
            """<path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/><path d="m15 5 4 4"/>""",
        [IconName.ExternalLink] =
            """<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>""",
        [IconName.Info] =
            """<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>""",
        [IconName.More] =
            """<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>""",
        [IconName.Pause] =
            """<rect x="14" y="4" width="4" height="16" rx="1"/><rect x="6" y="4" width="4" height="16" rx="1"/>""",
        [IconName.Play] =
            """<polygon points="6 3 20 12 6 21 6 3"/>""",
        [IconName.Plus] =
            """<path d="M5 12h14"/><path d="M12 5v14"/>""",
        [IconName.Remove] =
            """<path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/><line x1="10" x2="10" y1="11" y2="17"/><line x1="14" x2="14" y1="11" y2="17"/>""",
        [IconName.Search] =
            """<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>""",
        [IconName.Send] =
            """<path d="M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z"/><path d="m21.854 2.147-10.94 10.939"/>""",
        [IconName.Settings] =
            """<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>""",
        [IconName.Stop] =
            """<rect width="18" height="18" x="3" y="3" rx="2"/>""",
        [IconName.Warning] =
            """<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>""",
    };

    #endregion

    #region Public Methods

    public static string For(IconName name) => shapes[name];

    #endregion
}
