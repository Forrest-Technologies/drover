# The native sandbox screen

Captured on 2026-11-18 from the desktop build (`net11.0-desktop`, Debug) on Linux under a virtual X11 display, driven
with real pointer and key events (`xdotool`).

The server is a stand-in, not Drover's server and not a sandbox provider: a small script that answers the addresses
the screen asks (`/api/antiforgery`, `/api/session`, the bot, `.../sandbox`, `.../sandbox/screen`,
`.../sandbox/input`, the power controls, `.../sandbox/commands`) over HTTPS, through the app's own transport. The
sandbox's screen is a test pattern marked as a sample: a grid labeled with the sandbox's pixel coordinates, a colored
square in each corner, a cross where each click arrived with its coordinates, and the text, keys, and scroll steps
that arrived. Every name is a sample.

| Picture | What it shows |
| --- | --- |
| `sandbox-paused-*` | A paused sandbox: nothing is asked of the screen, and one button resumes it |
| `sandbox-view-*` | After that button: the picture, view only |
| `sandbox-control-*` | Send desktop input on: the frame in the person's color, the keyboard pointer, the input controls |
| `keyboard-pointer-moved-1280-dark` | A click at 512, 203, then Right, Right, Shift+Down, Enter: a second click at 544, 267 |
| `keyboard-tab-left-the-picture-1280-dark` | Tab from the picture: the focus is on the next control |
| `view-only-enlarged-focused-1280-dark` | View only at 200 percent, reached with Tab: the focus ring on the picture |
| `key-combinations-1280-dark` | The key combinations menu |
| `zoom-200-1280-dark`, `zoom-dragged-1280-dark` | Enlarged with the buttons, then dragged with the mouse |
| `screen-failing-1280-dark`, `screen-recovered-1280-dark` | The server refuses the picture, then answers again |
| `terminal-command-1280-dark` | A command run from the terminal tab |
| `rebuild-dialog-1280-dark` | The dialog that asks before rebuilding |

Each `*` is `1280-dark`, `1280-light`, `360-dark`, `360-light`.
