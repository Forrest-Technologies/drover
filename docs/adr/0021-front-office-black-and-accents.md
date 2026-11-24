# 0021. Front Office is the default, every theme has a black mode, and a person chooses their accent

- Status: Proposed in its pull request
- Date: 2026-11-24
- Source: the owner's ask of 2026-11-24, with a picture of the look wanted. Follows tracking issue [785](https://github.com/Forrest-Technologies/drover/issues/785)
- Supersedes: [0020](0020-a-family-of-themes.md) where it says Night Yard stays the default, that a theme has two modes, and that the color of a person's intent is one fixed color per theme

## Context

[ADR 0020](0020-a-family-of-themes.md) made the look a family of themes and kept Night Yard, the charged look with
faceted corners and a dot yard, as the default. The owner then asked for the default to be a professional look: neutral
near-black surfaces, round shapes, both sides of a conversation in bubbles, the bot heads kept. The same ask named three
things the theme model could not say:

- A look with large round corners. The corner languages were faceted, square (radius 8 at most) and ticked.
- A conversation where the bot speaks in a bubble too. Night Yard's replies run the full width as a document.
- A mode for screens that turn their pixels off, and a choice of accent color.

Two standing preferences of the owner came with it and are about the interface, not about a theme: nothing stays on
screen that can be shown when it is needed, and what a reply used belongs in a status line, not under every reply.

## Decision

- **Front Office is the default theme**, in the `professional` group. A device that never chose a theme gets it, dark.
  Night Yard stays in the app, unchanged, and is the theme whose values `tokens.css` and `Tokens.xaml` hold.
- **The format gains two values and one key.** `shape.corners` takes `rounded`, with radii large enough that a control
  at its smallest pointer height is a pill. `surfaces.conversation` is `document` or `bubbles`. Both are bounded like every
  other value; neither is a gradient or a shadow.
- **Every theme has a black mode, and no file stores it.** Black is the dark mode with a black first surface and each
  surface above it taking the color of the one below. Everything drawn on a surface keeps its color, so it stands on a
  darker surface than the one it passed on. The gates run over black as they do over dark and light.
- **A person may turn the hue of their own intent.** `ember` keeps its meaning, a person's intent, in every theme. A
  person may choose an accent for it from a short fixed list. The color is worked out for the theme and the mode at the
  theme's own chroma, and an accent is offered for a theme only when the theme then passes every gate, the distance in
  hue from `ion` and `violet` included. `ion` and `violet` are never a person's to change.
- **A stored choice made before a person could choose a theme named no theme.** It keeps its mode and takes the
  default. From this change on the stored object carries a version, and a theme it names is a choice.
- **Reply actions show when they are needed**: on the newest reply, and on an older one when it has the focus or is
  tapped. **What a reply used is said once**, in the status line under the composer that
  [docs/ux/conversation-display.md](../ux/conversation-display.md) specifies; this change builds the first part of it.

## What does not change

Light means live, the three accent roles and what each means, the four live states and their static equivalents, depth
by surface and line only, both stored modes shipped and passing, the character parts, and everything under "What
Drover's interface never does". A bot is still a head with a visor in every theme.

## Consequences

- `DESIGN.md` describes Front Office first and Night Yard as the first expressive theme.
- A theme file needs `surfaces.conversation`. The seven files of ADR 0020 say `document`.
- The professional presets of [issue 788](https://github.com/Forrest-Technologies/drover/issues/788) (Slate, Linen,
  Graphite) are still the owner's to decide. Front Office does not replace them.
- New kinds of card a bot could send (choices to pick from, a board of figures) are not part of this decision. They
  need a wire format and are their own issue.
- The sample sheet of issue 421 still draws the seven themes of ADR 0020. Front Office is not on it; its pictures are
  the app's own, in `docs/screenshots/front-office/`.
