# 9. Design direction

The one-page version of this section, written for anyone building a screen, is [`/DESIGN.md`](../../DESIGN.md). It also holds the list of habits Drover's interface never falls into and which of them the build checks.

Working name for the look: **Night Yard**. A dark workspace, calm at rest, where the bots are the lit things. Human intent is warm, bot activity is cool, and nothing glows unless it is doing something. What is working is turned up: it carries light (bloom), not only color. The owner chose this charged look over a flat one on 2026-11-12.

## Principles

1. **Light means live.** Color and motion mark what is working right now. Everything at rest is neutral.
2. **Show the work.** Tool activity and the bot tree are content, not debug panels.
3. **One thing at a time.** A screen starts with what most people need. Controls appear when a choice makes them relevant.
4. **Fast first.** Every effect has a job: showing state or confirming an action. If removing it loses no information, remove it.

## Tokens

One file, `src/Drover.Native/Theme/tokens.css`, as CSS custom properties: the specification. The native client carries every token in `Theme/Tokens.xaml` under a predictable name, and a test fails when the two differ ([18-native-client.md](18-native-client.md), "Tokens"). Controls reference semantic tokens only, never raw values.

### Color, dark (primary)

| Token | Value | Use |
| --- | --- | --- |
| `--dv-bg-0` | `#05070A` | App background |
| `--dv-bg-1` | `#0B0E14` | Panels, sidebar |
| `--dv-bg-2` | `#121722` | Cards, message bubbles |
| `--dv-bg-3` | `#1B2230` | Hover, selected |
| `--dv-line` | `#263042` | Dividers, decorative borders |
| `--dv-text` | `#E9EDF5` | Primary text |
| `--dv-text-2` | `#AEB7C7` | Secondary text |
| `--dv-text-3` | `#8791A5` | Captions, timestamps |
| `--dv-ember` | `#FFB454` | Human intent: primary buttons, focus ring, the send action |
| `--dv-ion` | `#67E8F9` | Bot activity: thinking, streaming, running tool |
| `--dv-violet` | `#B4A0FF` | Delegation between bots |
| `--dv-ok` | `#4ADE80` | Success |
| `--dv-warn` | `#FACC15` | Needs attention, approval waiting |
| `--dv-danger` | `#FF7A7A` | Failure, destructive action |

### Color, light

| Token | Value |
| --- | --- |
| `--dv-bg-0`, `-1`, `-2`, `-3` | `#F6F7FA`, `#FFFFFF`, `#EEF0F5`, `#E2E6EE` |
| `--dv-line` | `#D5DAE3` |
| `--dv-text`, `-2`, `-3` | `#12151C`, `#414B5E`, `#5D6779` |
| `--dv-ember`, `--dv-ion`, `--dv-violet` | `#8F5400`, `#0B6E82`, `#5B3FD6` |
| `--dv-ok`, `--dv-warn`, `--dv-danger` | `#15703A`, `#7A5A00`, `#B3261E` |

### Contrast, computed

Ratios against the WCAG formula, calculated for these exact values:

| Foreground | Dark, on `bg-0` to `bg-3` | Light, on `bg-0` to `bg-3` |
| --- | --- | --- |
| `text` | 16.7 to 12.8 | 4.5 or higher on every surface; the lowest pair is `text-3` on `bg-3` at 4.56 |
| `text-2` | 9.7 to 7.4 | |
| `text-3` | 6.2 to 4.7 | |
| `ember` | 11.1 to 8.5 | 4.89 at lowest |
| `ion` | 13.5 to 10.3 | 4.71 at lowest |
| `violet` | 8.8 to 6.7 | 5.37 at lowest |
| `ok`, `warn`, `danger` | 7.8 or higher on `bg-0`, 5.9 or higher on `bg-3` | 4.92 at lowest |

Light `bg-3` was missing from the first version of this table. Adding it put the first light `ember` (`#9A5B00`) at 4.34 on that surface, so light `ember` is `#8F5400`.

Every text and status color clears 4.5:1 on every surface in both themes. `--dv-line` is about 1.5:1 and is decorative only; any border that conveys a control's edge or state uses `--dv-text-3` or a status color, which clear the 3:1 needed for interface elements. A unit test recomputes this table from `tokens.css` and fails if a change drops a pair below its threshold.

### Bot identity color

Each bot gets a hue derived from its id, carried by the head of its character (`--dv-head-0` to `--dv-head-11`, the same in both themes, each clearing 3:1 against the visor at rest, lit, and failed; a test recomputes it) and used as its lane color in the activity view and tree. The hue is picked from twelve fixed steps, `--dv-hue-0` to `--dv-hue-11`, thirty degrees apart at one lightness and chroma per theme, and every step clears 4.5:1 on every surface (7.1 or higher in dark, 4.6 or higher in light). Color is never the only signal: the bot's name or initials always accompany it.

### Type

| Token | Value |
| --- | --- |
| `--dv-font-ui` | Inter (variable), then the system UI stack |
| `--dv-font-mono` | JetBrains Mono, then the system monospace stack |
| Scale | 12, 13, 14 (base), 16, 20, 28 px, with line heights 1.5 for text and 1.25 for headings |
| Weights | 400 body, 500 labels, 600 headings |

Both fonts are bundled (SIL OFL 1.1). Nothing loads from a third-party host, which matters for privacy on self-hosted servers and for the apps offline. Monospace is used for tool arguments, commands, and output, so "what the machine did" always looks different from "what was said".

### Space, shape, depth

| Token | Value |
| --- | --- |
| Spacing | 4 px base: 4, 8, 12, 16, 24, 32, 48 |
| Radius | 6 (controls), 10 (cards), 16 (sheets), full (avatars, pills) |
| Elevation | Two levels only, done with a lighter surface and a 1 px line, not heavy shadows |
| Hit target | 44 px minimum on touch, 32 px on pointer |

### Motion

| Token | Value | Use |
| --- | --- | --- |
| `--dv-dur-1` | 120 ms | Hover, press, small state changes |
| `--dv-dur-2` | 220 ms | Panels, sheets, list insertions |
| `--dv-dur-3` | 420 ms | Delegation handoff, tree layout changes |
| `--dv-ease` | `cubic-bezier(0.2, 0.8, 0.2, 1)` | Everything that moves |
| `--dv-dur-breath`, `--dv-dur-travel` | 2600 ms, 1300 ms | One cycle of the thinking breath and of the tool line. These loop only while the state lasts. |

### Light

| Token | Value | Use |
| --- | --- | --- |
| `--dv-bloom-ion` | An 8 px soft light in `ion` | Around a thinking bot and a running tool's line |
| `--dv-bloom-violet` | The same in `violet` | Around a handoff pulse |

Bloom is used only inside the four state classes below. It is never used for depth, hover, or emphasis.

### Theme scope

The dark values sit on `:root` and on any element marked `data-theme="dark"`; the light values on `data-theme="light"` and, when the person's system asks for light and no theme is forced, on `:root`. A subtree can therefore show the other theme, which is how the gallery shows both at once.

## Themes

The look is a family of named themes ([ADR 0020](../adr/0020-a-family-of-themes.md)). A theme has a dark and a light mode and sets seven token groups. Night Yard, described above, is the default and the identity used for the mark, screenshots and documentation; every value in this file above this section is Night Yard's.

### The invariants

No theme may change these. They are not tokens, so a theme file has no way to say them:

- The three accent roles and their meanings: `ember` is a person's intent, `ion` is a bot working, `violet` is work passing between bots.
- Light means live. Only the four live states move, bloom appears only inside them, and each has a static equivalent.
- Both modes ship and both pass every check.
- Everything in "What Drover's interface never does" in `DESIGN.md`, including no drop shadows, no blur panels and no decorative gradients: depth is surface and line only.
- Every screen works at 360 px and at 200 percent text; reduced motion leaves nothing animating.
- The character parts and a bot's stored look never change with the theme, only how they are drawn. A bot is always a head with a visor.

A look that needs to break one is not a theme. It is a change to `DESIGN.md` with an ADR.

### The token groups and their bounds

| Group | What a theme sets | Bounds |
| --- | --- | --- |
| Color | Per mode: surfaces `bg-0` to `bg-3`, `line`, `text`, `text-2`, `text-3`, `ember`, `ion`, `violet`, `ok`, `warn`, `danger`, `on-accent`, the twelve hues, the twelve heads, `on-head`, `visor`, `visor-lit`, `visor-failed` | Text, accent, status and every hue 4.5:1 on every surface; control edges (`text-3`) 3:1 on every surface; `on-accent` 4.5:1 on `ember`; the visor 3:1 against every head at rest, lit and failed |
| Type | Interface face, machine face, optional reading face for conversation text, which face sets headings, machine text for timestamps and counts, the six-step scale, weights, leading, tracking, measure | Base (the third step) 13 to 16; every face bundled under an accepted license; machine text always a different face from conversation text; sentence case |
| Density | Spacing multiplier over the 4 px base, control height, list row height, composer lines | Multiplier 0.85 to 1.25; control and row at least 44 on touch and 32 on pointer at every density |
| Shape | Corner language (`faceted`, `square`, `ticked`), cut or tick sizes, registration marks, radii, line weight, icon stroke | Line 1 or 2; the shape class is the only place corners are drawn |
| Surfaces | Surface levels (2 or 3), yard texture (`dots`, `lines`, `grid`, `none`), its strength and step | Depth by surface and line only |
| Motion | Duration scale, bloom, breath and travel cycles, how a live state shows (`loop` or `fade`), travel line weight | Scale 0.8 to 1.3 over 120, 220 and 420 ms; bloom 0 to 8; cycles 0.8 to 1.3 times 2600 and 1300 ms |
| Characters | Head lightness and chroma, facet highlight, part line weight, visor style (`band`, or `outlined` for themes without bloom) | Parts and stored look never change |

### The first batch

All seven are in the `expressive` group. The `professional` group is defined in the format and holds no theme yet.

| Theme | Intent | What makes it different |
| --- | --- | --- |
| Night Yard | A dark, calm workspace where the bots are the lit things | Today's look: Inter at 14, faceted cuts 6 and 10, two surface levels, dot yard, bloom 8, standard motion |
| Signal Room | Dense and exact, for people running many bots who want more on screen | Spacing 0.85, base 13, rows 32 on pointer, square corners at radius 2, hairline lines, three surface levels, no yard, timestamps and counts in machine text, motion 0.8, bloom 4. Near-black blue; light is cool gray paper |
| Paper Ledger | Warm paper and a reading face, for long reading and writing in conversation | Light first. Literata for conversation text only, base 16, measure 68, ruled-line yard at low strength, ticked corners, bloom 0 with an outlined visor lit by fill, motion 1.2. Dark is warm brown-black ink |
| Workbench | Large, high-contrast and touch-first, for phones, bright rooms and low vision | Atkinson Hyperlegible Next at base 16, spacing 1.2, 2 px lines and control edges, faceted cuts 8 and 14, every text color 7:1 or higher, no yard, bloom 6. The theme to suggest when a device asks for large text |
| Blueprint | Deep blue and a drawing grid, for people who like precise structure | Thin grid yard, ticked corners drawn as registration marks, headings in the machine face at 500, a strong travel line with bloom 3, motion 0.9 |
| Quiet Hours | Low stimulus: soft color, no light effects, and every live state said in words | Low-chroma accents that still pass, bloom 0, live states as a single fade instead of a loop, durations 1.3, leading 1.6, square corners at radius 6, no yard. The theme to suggest when a device asks for less motion |
| Foundry | Warm and heavy, soot and brass, for people who find the default cold | Charcoal and soot, ember leaning brass, ion a pale teal, headings at 600 with tighter tracking, faceted cuts 10 and 16, line yard at low strength, bloom 8. Light is warm stone |

Every pair is computed in [`docs/design/themes/contrast.md`](../design/themes/contrast.md) (`python3 scripts/themes/contrast.py`). The sample sheet, [`docs/design/themes/sample-sheet.html`](../design/themes/sample-sheet.html) (`python3 scripts/themes/sample-sheet.py`), draws the conversation with a running tool and an approval card, the crew and the bot editor in every theme and mode at desktop width and 360 px, and twenty-four characters per theme; its pictures are in `docs/screenshots/themes/`. Everything in it is a labeled sample.

Two faces are added, both SIL OFL 1.1: Literata (Paper Ledger's reading face) and Atkinson Hyperlegible Next (Workbench's interface face). Each is fetched only when its theme is chosen where the platform allows; the theme issues that ship them record the download size.

### The gates

Every theme must pass the same accessibility bar as Night Yard, and the build enforces it, not review. The gates run over every file in `src/Drover.Presentation/Theme/Themes`, in both modes, on the resolved values: a new theme file that fails one fails the build and cannot merge. A failure names the theme, the mode, the pair and the ratio (`night-yard, dark: TextContrast: text-2 on bg-3 is 3.90:1, needs 4.5:1`). `ThemeGates` (in `Drover.Presentation`) holds them, and `ThemeGateTests` runs them over every file and over a deliberately broken theme for each gate, so a gate that stopped checking fails too. The shared checker of [issue 778](https://github.com/Forrest-Technologies/drover/issues/778) runs the same gates on a person's own theme before anything is applied.

| Gate | Check | Where it runs |
| --- | --- | --- |
| Text contrast | Every text and status color (`text`, `text-2`, `text-3`, `ember`, `ion`, `violet`, `ok`, `warn`, `danger`) 4.5:1 on every surface; `on-accent` 4.5:1 on `ember` | `ThemeGates`, unit tests |
| Interface contrast | Control edges (`text-3`) and the focus ring (`ember`) 3:1 on every surface | `ThemeGates`, unit tests |
| Bot hues | Each of the twelve hues 4.5:1 on every surface; each head 3:1 against the visor at rest, lit and failed | `ThemeGates`, unit tests |
| Bounds | Every value inside the bounds of the table in "Theme files": density, base size, durations, bloom, radii and the rest | `ThemeFileReader` through `ThemeGates`, unit tests |
| Touch targets | Control and list row heights at the theme's density give 44 px for a finger and 32 px for a pointer | `ThemeGates`, unit tests |
| Invariants | `ember`, `ion` and `violet` are at least 45 degrees apart in the Oklab hue in each mode; no key of a theme file names a gradient or a shadow | `ThemeGates`, unit tests |
| Reduced motion | With reduced motion set, no element animates in any theme: the four live states are still | Screen checks over the four live states |
| Text scaling | At 200 percent text scale and 360 px width, the conversation, crew, bot editor and approval card have no sideways scroll and no clipped text | Screen checks |

The WCAG formula is the one the contrast tests use. The line color is decorative and exempt, as in "Contrast, computed". The nine text and status colors and the control edge are the only colors the Interface contrast and Text contrast gates name; a theme's own tighter rule, such as Workbench's 7:1 on every text color, is part of its design and is read from [`docs/design/themes/contrast.md`](../design/themes/contrast.md).

### Theme files

A theme is one JSON file, `src/Drover.Presentation/Theme/Themes/{id}.json`. The format is closed: every key below is required unless marked optional, no other key is allowed, and the types and bounds are exact. The pipeline ([issue 422](https://github.com/Forrest-Technologies/drover/issues/422)) applies these files, the gates ([issue 423](https://github.com/Forrest-Technologies/drover/issues/423)) and the shared checker ([issue 778](https://github.com/Forrest-Technologies/drover/issues/778)) read the bounds from this table, and a person's own theme ([issue 778](https://github.com/Forrest-Technologies/drover/issues/778)) uses the same shape.

| Key | Type | Bounds and meaning |
| --- | --- | --- |
| `format` | integer | `1` |
| `id` | string | Lowercase words joined by `-`, the file's name without `.json` |
| `name` | string | 1 to 40 characters, plain text, sentence case words; names the look, never a product |
| `intent` | string | One sentence, plain text, at most 120 characters; shown under the name where a theme is chosen |
| `group` | `expressive` or `professional` | Where the Appearance page lists the theme ([issue 424](https://github.com/Forrest-Technologies/drover/issues/424)). `expressive`: a look with a character of its own. `professional`: a restrained look for work settings. Grouping only; it changes no token and no bound |
| `firstMode` | `dark` or `light` | The mode the theme is shown in first where a person has not chosen one |
| `suits` | list of names | Zero or more of `large-text`, `reduced-motion`: device requests the theme is suggested for |
| `type.interface` | face id | The interface face |
| `type.machine` | face id | The machine-text face; never the same as `interface` or `reading` |
| `type.reading` | face id or `null` | The face of conversation text; `null` uses `interface` |
| `type.headings` | `interface` or `machine` | Which face sets headings |
| `type.machineNumerals` | boolean | Timestamps and counts in the machine face |
| `type.scale` | six numbers | Ascending px sizes of the six steps; the third is the base, 13 to 16; the smallest at least 11 |
| `type.weights.body`, `.label`, `.heading` | integers | 400 to 700 in steps of 100; body below label, label at most heading |
| `type.leading.text`, `.heading` | numbers | Text 1.3 to 1.7, headings 1.1 to 1.4 |
| `type.tracking`, `type.headingTracking` | numbers | Em: -0.02 to 0.04 |
| `type.measure` | integer | Characters per line of reading text, 55 to 80 |
| `density.spacing` | number | 0.85 to 1.25, over the 4 px base steps 4, 8, 12, 16, 24, 32, 48, each rounded to a whole pixel and at least 2 |
| `density.control.pointer`, `.touch` | integers | Smallest control height: pointer 32 to 48, touch 44 to 56 |
| `density.row.pointer`, `.touch` | integers | Smallest list row height: pointer 32 to 56, touch 44 to 64 |
| `density.composerLines` | integer | Lines the composer grows to, 4 to 12 |
| `shape.corners` | `faceted`, `square` or `ticked` | The corner language the shape class draws |
| `shape.cut.control`, `.frame` | integers | `faceted`: cut sizes, control 4 to 10, frame 8 to 16. `ticked`: tick lengths in the same ranges. `square`: both 0 |
| `shape.registration` | boolean | `ticked` only: the ticks cross out past the corner as registration marks. `false` for the others |
| `shape.radius.control`, `.card`, `.sheet` | integers | `square`: 2 to 8, 2 to 12, 4 to 16. `faceted`: radii for what is round by nature, as today. `ticked`: 0 |
| `shape.line` | integer | 1 or 2: decorative lines and control edges |
| `shape.iconStroke` | number | 1.5 to 2 on the 24 unit icon grid |
| `surfaces.levels` | integer | 2: lists and side panels rest on `bg-0`. 3: they rest on `bg-1`, a level of their own |
| `surfaces.yard.texture` | `dots`, `lines`, `grid` or `none` | The working area's texture |
| `surfaces.yard.strength` | number | 0 to 0.7: the share of `line` the texture is drawn in; 0 with `none` |
| `surfaces.yard.step` | integer | 12 to 32 px |
| `motion.scale` | number | 0.8 to 1.3 over 120, 220 and 420 ms |
| `motion.bloom` | number | 0 to 8 px; 0 needs `characters.visor` `outlined` |
| `motion.breath`, `motion.travel` | integers | ms; 2080 to 3380 and 1040 to 1690 |
| `motion.live` | `loop` or `fade` | `fade`: thinking and a running tool show as one fade to a still state, said in words, never a loop |
| `motion.travelLine` | integer | 2 or 3 px |
| `characters.head.lightness`, `.chroma` | numbers | OKLCH of the twelve heads: lightness 0.4 to 0.6, chroma 0.04 to 0.12; the heads in `color` are these at hues 0, 30, ... 330 |
| `characters.facet` | number | 0 to 0.15: the light face drawn on the head's left half |
| `characters.line` | number | 2 to 3.5 on the 64 unit grid |
| `characters.visor` | `band` or `outlined` | `outlined` draws the visor's edge so it reads without bloom |
| `dark`, `light` | objects | One per mode, each `{ "hues": { "lightness", "chroma" }, "color": { ... } }` |
| `{mode}.hues.lightness`, `.chroma` | numbers | OKLCH of the twelve hues in that mode |
| `{mode}.color` | object | Exactly the 43 color tokens of the Color row above, each `#RRGGBB`, named as in `tokens.css` without `--dv-` |

Face ids are the faces Drover bundles: `inter`, `jetbrains-mono`, `literata`, `atkinson-hyperlegible-next`. A file never names a font file, a URL or a path. Derived values are not stored: the scrim is `bg-0` at 72 percent, the yard color is `line` at the yard strength, spacing steps come from the multiplier, durations from the scale. The blocks group of [issue 783](https://github.com/Forrest-Technologies/drover/issues/783) will be added to this format by that issue.

A new theme is a new file in that folder that passes the checks; nothing else names it.

## Motion rules

1. **Motion shows state, nothing else.** The four states that earn motion, each a class in `base.css` (`dv-state-thinking`, `dv-state-streaming`, `dv-state-tool`, `dv-state-delegation`):

   | State | Motion |
   | --- | --- |
   | Bot thinking | A slow breath of light across the bot's visor. No bouncing dots. |
   | Text streaming | Text appears as it arrives. The last few characters fade in over 120 ms so chunks do not flicker. No typewriter effect that delays text already received. |
   | Tool running | A thin ion line travels along the top edge of the tool card. It stops and turns green or red when the tool finishes. |
   | Delegation | A violet pulse travels the connector from parent to child in the tree and in the activity rail, once, when the handoff happens, and back when the result returns. |

2. **Nothing loops unless something is in progress.** When the work stops, the motion stops.
3. **Nothing blocks.** No animation delays input or hides content that has arrived. Durations stay under half a second.
4. **Reduced motion is respected.** Under `prefers-reduced-motion: reduce`, travel and pulse animations are replaced by an instant state change plus a static indicator: a solid ring for thinking, a "running" label for tools, a highlighted connector for delegation. No information is lost.
5. **Layout does not jump.** Streaming text grows downward in a container that keeps the scroll position pinned only while the user is at the bottom.

## Key screens

### 1. Conversations

Its job: pick the bot to talk to, and talk to it in its one main conversation.

A person talks to a bot, not to a thread. Three regions on a wide window: the crew, the main conversation with the chosen bot, and, only when asked for, that bot's side chats. A phone shows one of the three at a time. The crew and other workspace pages share a bottom dock for Chats, Bots, Tools, and Account. In a conversation, one menu opens the bot’s summary, memory, screen, files, and side chats; the composer keeps the bottom of the screen.

- **The crew** is the first column: the space's bots as the hierarchy they form, each under the bot that manages it, joined by a thin connector. A row is the bot's head at 32 px, its name, "Paused" in words when it is, and one word for what the bot has for this person: "Waiting for you" (a run of theirs waits for their answer), "Failed" (their newest run failed and they have not looked since), or "New" (a reply they have not seen), in that order, only the first that applies. The word carries the attention or failure color, is part of the row's spoken name ("Marshal, waiting for you"), and never glows or moves: these are facts, not live states. A side chat shows its own word the same way, so a word in the crew can be traced to the chat it came from. The browser tab's title starts with how many bots wait for the person, "(2) Marshal · Drover". The head's visor breathes while the bot works for this person. Choosing a bot opens its main conversation; the bot being talked to is framed. The start page talks to the lead: the first bot at the top of the tree that can answer.
- **The main conversation** is the one continuing conversation a person has with a bot. The server keeps exactly one for each person and bot and opens it through the bot, so there is no list to pick from and nothing to name. Its header is the bot: the head at 48 px, the name as the page's heading, and one way to the side chats that says how many there are.
- **Side chats** are the other conversations with the same bot, for something that should stay out of the main one. They are the exception and the interface keeps them that way: nothing of them shows until the person asks, a bot always opens on its main conversation, and the rail says in one sentence what a side chat is. From 1200 px the rail sits to the right of the main conversation, which stays in use beside it. Below that the side chats take the conversation's place, with one control back: from a side chat to the list, from the list to the main conversation. A new side chat gets its thread from its first message.
- **The summary** is the main conversation's second form: the text the server keeps of everything said so far and rewrites as the conversation goes on. It opens in the same place the side chats do, by its own way in the header, and says how many messages it covers and when it was written. While it is open it is read again each time the bot stops working. Before there is one, the rail says when one is written. The person never edits it.
- **Memory** is what the bot remembers about the person reading: every note it keeps about them, newest change first. It opens in the same place as the summary and the side chats, by its own way in the header and its own address (`/talk/{bot}/memory`), and is read again each time the bot stops working. Each note says who wrote it and when ("Marshal noted this on 12 November", with a link that opens the conversation at that message and brings it into view, or "You wrote this"). Choosing Change turns the note into a field with Save and Cancel, and the focus moves into the field and back to the note. Remove asks nothing and offers Undo in a toast ("Undo removing the note: …"), which puts the note back exactly; the toast stops offering Undo when the server's ten-minute receipt runs out. "Add a note" is one field at the top. The notes' size is said only when they are nearly full, as a sentence. Memory off says so, and sends an owner or admin to the Memory section of the bot's editor; notes kept from before are still listed, with Remove alone, under a sentence saying they stay until removed and no new ones are added. With no notes, the rail says when the bot keeps one.
- **Files** is where a person finds a file again: every file they and the bot exchanged in their conversations with it, newest first, opened by its own way in the header, in the same place as the summary. Each says who it came from ("From Marshal", "From you"), when, and links to that moment in its conversation, where the message is brought into view and takes focus. The one filter, from whom, shows only when files came from both sides. Before there is one, the rail says that files the person attaches and files the bot shares are listed there. It is read again each time the bot stops working. A file a bot shares shows in its reply the way a person's does: a picture at its size, a text file's first lines with "Show all", anything else a card with Download, and the bot's caption under it.
- A bot that asks to make a bot is answered on one card that says everything about the new bot in words: its name, what it is for, its instructions, the model it will use, and the bot it will work beneath. Making a bot always asks, whatever the tool is set to; the Tools section says so and does not offer Allow for it.
- Removing a bot that has bots beneath it asks whether they are removed too or kept. Kept bots move up under the removed bot's parent, and the dialog names it.
- The crew is read again whenever a bot stops working, so a bot the lead has just made is there without a reload.
- An address kept from before (`/threads/{id}`) goes to where that conversation is now: the bot's main conversation or its side chat.
- The activity rail shares the right side with the side chats, the summary, and the screen, with its own address (`/talk/{bot}/activity`, and `/talk/{bot}/activity/{run}` for an earlier run). The header opens it on the newest run; a run's last reply has "See what happened", which opens that run. From 1200 px it shares the width evenly with the conversation, as the screen does. On a phone it takes the conversation's place with one control back.

- The conversation is quiet. User messages sit right in a `bg-2` bubble; bot messages run full width with no bubble, like a document. Markdown renders live.
- Tool use appears inline as a compact card after the words of the reply that made the call: what the bot did in plain words ("Ran a command", "Wrote a file", "Took a screenshot"; another tool goes by its title), a one-line summary of the arguments, the state in words (running, done, failed, waiting for you, interrupted with the outcome unknown), and a state edge. Tapping it opens the card in place, a disclosure: the arguments, the output in monospace as it streams, error output marked "stderr" in words as well as color, in a bounded box that keeps its newest line in view, and the result with the exit code where there is one. A command that exits nonzero or times out shows Failed. A failed command previews its error output when available. A screenshot is not kept, so its card only says the bot took one. A note the bot saves reads as the note itself, "Remembered: Prefers tea.", with one button, "Forget", which removes the note only while it still says what the card shows; the button is then replaced by a line saying the note is no longer kept, the focus moves to it, and a toast offers Undo as the rail does. A note changed since the card was drawn is left alone, and the card says so with a link to Memory. A changed note reads "Updated a note" with before and after in the disclosure, and a removed one "Forgot: " and the note. A reopened thread shows the same cards, rebuilt from the runs' logs. The activity rail shows the same call on the run's timeline, with its arguments, output and result.
- Work a bot hands to a bot beneath it is a card in place of the `delegate` call's tool card, with the child's head at 32 px beside it. Closed, it is one sentence, "Marshal asked Scout: <first line of the task>", and the state in words: working, waiting for you, done, failed, or cancelled. Its edge is violet while the work is out, then green or red as it ended. Opened in place it shows the whole task, then the child's own words and tool cards in order as they stream, the same components the conversation uses, set in by one line under the child's head, then the child's answer as it was returned, rendered as markdown like any bot's words. Work the child handed further nests one more level; past two levels a card says the work is on the child's side and links there instead of nesting. "Open Scout's side of this" opens the child's thread for the work in the rail beside the conversation, to read: no field to write in, and closing the rail is the way back. While the work is out it follows the child live, on the tree's stream; a card past two levels points there only when there is a thread to open. A call the child needs answered is that call's own card inside the delegation card, "Scout asks to run a command", and the card opens itself when the child starts waiting so the buttons are in view. A delegation the server refused (a loop, a limit of the space, a paused or removed bot, a bot that may not be handed work) is a card that says which in a sentence. Until the server has begun the work (while the person is asked whether the bot may hand it over), the call is an ordinary tool card naming the bot and the task. A reopened thread shows the same cards, rebuilt from the stored events.
- In the crew, while work is out, the connector beside the bot that has it holds violet, with one pulse as the work goes out and one as it comes back; that bot's visor breathes while it works for this person. With reduced motion the connector holds violet while the work is out and nothing pulses.
- The **activity rail** is the run as a vertical timeline. Each bot in the run gets a lane headed by its head at 24 px and its name, with a rail in its identity color; every node's spoken name starts with its bot, so the hue is never the only way to tell lanes apart. Steps, tool calls, waits for a person, rules that fired, and delegations are nodes. A delegation branches into the child's lane in violet ("Handed work to Scout"), and its answer comes back across at the row it returns on ("Got the answer from Scout"). The node being worked on carries its live state: the traveling line for a tool, a breathing edge for a step being thought through, one pulse along the branch while work is out; nothing else moves. Selecting a node opens it in place under it, across every lane: what a step wrote (through the markdown view, as untrusted text), a call's arguments, output in monospace as it streams, the result, how long it took, and the cost of a model call. A sandbox command reads as a terminal: the command, then what it printed. A wait no one has answered opens to the bot's request in a sentence, with Approve and Deny described by it, wherever in the tree the bot is; the answer goes to the same decision endpoint the conversation's card uses. The whole tree comes from the root run's one stream, replayed from the start and then followed live, so a run watched live and the same run opened later lay out the same. A call the conversation's bot made itself shows its whole stored result; a call further down shows the start its event keeps, and says so. The timeline is one tab stop: up and down move in time, left and right to the nearest node in the next lane, Home and End to the ends, Enter opens. Its nodes are a list in time order for assistive technology, the same sentences the list form shows; the list form is one choice away. A long run shows its newest 200 events and pages back. States: no run yet, loading, and a run whose earliest events are no longer kept, each said in words.
- An approval request is the waiting call's own card, in the conversation where the call was made and so just above the composer while it is the newest thing: "Marshal asks to run a command", the arguments (every one the summary leaves out), and Approve or Deny, each described by that sentence. While the run waits for the answer, nothing moves: no thinking state, no line along the composer. A bar above the composer that can be reached without leaving it is not built yet.
- A failed run ends the conversation with one line: what failed, in a sentence that names the bot and says what to do, and one button, "Try again", which starts a new run from the same message. What the server reported is behind a disclosure, in monospace, never shown by default. The sentence is said aloud once when it appears. The line stays until a run after it is under way.
- The composer is one field. Attach, model override, and other options appear in a row only after the plus button is used. The plus button says whether the row is open.
- Files are attached with Attach, by pasting into the field, or by dropping them on the composer. Each goes to the server as soon as it is chosen and shows as a chip above the field: its name, then its size, "Uploading, 40 percent", or why it cannot go, with a named remove button. A file the bot cannot open (a picture for a model that cannot read pictures and no sandbox, or anything but text without a sandbox) is refused before sending, in a sentence naming the reason, as is one past the server's limits. Send waits until every file is uploaded and none is refused; a message with files needs no words. A new side chat gets its thread from its first file, as from its first message.
- In the conversation a picture shows at its own size (its width and height are set, so nothing jumps), a text file shows its first lines, and any other file is a card with its name, kind, and size; each has the same named way to download it.
- A cancel control replaces send while a run is active. It cancels the whole tree.

### 2. Crew

The bot hierarchy as a top-down tree on a pannable canvas. This is the first bold screen.

- Each bot is a node: avatar with identity ring, name, a one-line role, and a small live indicator. Connectors are thin lines.
- Live state is visible at a glance: a breathing ring where a bot is thinking, a violet pulse along a connector during a handoff, a warning dot where an approval waits.
- Selecting a node opens a side sheet: what the bot is doing now, recent runs, cost for it and its subtree, and Pause, Edit, Remove.
- A human can add a bot beneath any node. Caps (depth, width) show as a quiet counter on the parent, such as "3 of 5", and the add control disables at the cap with the reason in its label.
- Bots created by another bot carry a small "made by" mark.
- With a keyboard, the tree is an accessible tree view: arrow keys move, Enter opens. A list view with the same data is one toggle away and is the default for screen readers.

### 3. Bot editor

One page, sectioned, with progressive disclosure. Never a wall of settings.

- Always visible: name, avatar, "what this bot is for" (the description other bots read, labeled in those words), and instructions.
- Sections that open on demand, each showing a one-line summary when closed: Model, Tools, Rules, Memory, Routines, Sandbox, Team.
- **Tools** lists connections, each expandable to its tools. Every tool has a three-way control: Off, Ask, Allow. A tool the parent bot lacks is shown disabled with "Marshal does not have this". A changed tool definition shows a "review" badge with a diff.
- **Rules** holds checks the server makes every time, whatever the instructions say. Closed, it reads "No rules" or "3 rules, 1 off". A rule is built as a sentence: When (one of four moments), If (conditions, added one at a time, each showing only its own fields), Then (only the effects that moment can carry out). The builder shows the sentence as it is written, and a saved rule is listed and announced as that sentence, with a switch, change, and remove. Rules from the bots above are listed first, marked with the bot they come from, with no controls. Starters are labeled as starters and add a rule that can then be changed. The section opens by saying which conditions are exact and which catch common forms. The server does not run patterns a person writes (no preset does), so the section says so in one line and that choice is not offered; a rule that already has one keeps showing it. Every condition that looks for something in text carries "(catches common forms)" in its choice, in the sentence as it is written, in the saved rule's read-back, and in a trial it decides, and says beside its field what it does not catch. A list on named inputs is exact and starts on `to, cc, bcc`; a list of addresses anywhere in the input is its own choice and is marked a pattern. What the server notes about a rule (a tool name nobody has, a list that names no tool) is shown under the rule and read out when it is saved. "Try it" sends a sample to the server, which answers with the evaluator a run uses; the screen only words the answer, and a rule still being written is tried with the rest.
- **Routines** decides what this bot does on its own and when, and checks that it did. A routine is a prompt and when it runs. Closed, it reads "No routines" or "2 routines, 1 off"; empty, one sentence says what a routine is, with "Add a routine". A routine is listed and read out as one sentence, "Every weekday at 08:00, Berlin time", with its prompt's first line, when it runs next, and how its last run went in words ("Ran Mon 5 Jan, 08:00 and finished", "Failed ...: the reason"), linking to that conversation when the reader can see it. "When" is a choice of every day, weekdays, chosen days, a day of the month, every few hours, or custom, each showing only its own fields and the sentence it will read as; the time zone starts as the person's own. A custom expression is checked by the server as it is typed: its next times, or the sentence a save would refuse it with. A new routine is saved off: the person runs it once now, which is an ordinary run of theirs that leaves the schedule's next time alone, then turns it on. Only the person who made a routine changes what it sends or when; another owner or admin may run it once as themselves, turn it off, or remove it (removing asks first). A paused bot's routines say they do not run while it is paused.
- **Sandbox** offers three choices in plain words (Private, Shared with its team, Shared with chosen bots) and states what sharing means.
- **Team** holds the caps on bot building. Whether a bot may build bots at all is a grant like any other: the crew tools in the Tools section.
- Anything that needs setup says what is missing and links to it: "Add a sandbox provider to use this".
- The definition (everything but tools, rules, and routines) is saved together from a bar that appears once something has changed. A tool's grant, a rule, and a routine are saved as each is changed, because a grant or a rule takes effect at once.
- A closed section's summary sits inside the button that opens it, so it is read out with the section's name.
- A model the catalog knows cannot call tools replaces the Tools controls with the reason and where to change it.
- In Tools, built-in tools come first, then one group for each tool connection, also one that has listed nothing. A tool whose definition changed says the bot asks before each use until the change is accepted, shows what was last reviewed beside what it says now, and offers to accept it for every bot. A tool its server no longer lists says so.
- New bot opens the editor on a bot that is not made yet, at `/bots/new` (or `/bots/new?parent=` for New bot under a bot, so the draft keeps its parent), on the connection that last answered, with the name "New bot" and a handle that follows the name. Nothing is stored until the person saves: leaving asks first, with the unsaved-changes prompt, and creates no bot. Only the sections that need no stored bot are shown (Model, Memory, Team, Limits); the others appear once it is made. Saving makes the bot once with the name, model and parent chosen, then sets the rest of the draft on it, and opens it. The handle is the one the server made from the name, numbered if taken, unless the person chose one. There is no separate creation form.

### 3a. Tool connections

Its job: add a tool server, see whether it is connected, sign in to it, review changed tools, remove it.

- One frame for each server: its name, its address and facts in monospace, and its state in words (Connected, Not connected with the last error, Not checked yet).
- The add form asks for a name and an address, then how Drover proves who is calling: nothing, a header, or a sign-in. A credential's fields appear only under its choice, and a client id, secret, and sign-in service only when the person says they registered a client. Only what the final choice uses is sent.
- A secret is typed once and never shown back. A stored header has no field until it is being replaced.
- A sign-in that is missing or has lapsed is said in a sentence with the one button that fixes it. One sign-in for the whole space is an owner's or admin's to give; a person's own is theirs.
- The provider's page sends the browser back to the start page, which hands it to this screen; the screen says how the sign-in went.
- A changed tool shows what was last reviewed beside what it says now, and one button accepts it. Removing a server asks first and says what goes with it.
- The bot editor's Tools section links here when the space has no tool server.

### 4. Activity

Every run in the space, newest first, filterable by bot, status, and "waiting for me". Each row expands to the same timeline as the thread rail. This is where a human audits what the crew did overnight and answers approvals in bulk.

### 5. Setup and Status

Setup is three steps with one decision each: create your account, connect a model (base address and key, with a "test" button that reports the result in plain words), meet your first bot. Status is a single page of rows, each green, amber, or red with a sentence: database, migrations, each model connection, each tool connection, sandbox provider, push, background workers, key ring backup.

## Accessibility

Required for every component, and tested:

- Full keyboard operation, visible focus (a 2 px ember ring with 2 px offset), logical tab order, no keyboard traps.
- Names, roles, and states for assistive technology. Streaming messages use a polite live region that announces completed sentences, not every token. Tool state changes are announced once.
- Text contrast 4.5:1, interface elements 3:1, as computed above.
- Reduced motion, as above. Text scales to 200 percent without loss.
- Component tests assert roles and names; an automated accessibility check runs against the key screens in CI.

## Component approach

**Recommendation: Drover's own small component set on plain CSS with custom properties and Blazor CSS isolation.** This was the choice for the web client, which was retired on 2026-11-20. The native client keeps the same idea, Drover's own controls drawn from the tokens, on Uno Platform ([ADR 0015](../adr/0015-one-native-client.md), [18-native-client.md](18-native-client.md)). The table below is the original comparison.

| Option | Finding | Verdict |
| --- | --- | --- |
| Established styled library (several checked; all MIT or Apache-2.0 and healthy) | Each carries a strong look of its own. Themes change colors and corners, but spacing, density, motion, and component anatomy stay the library's. The two bold screens, tree and timeline, exist in none of them. | No. Drover would fight the library on exactly the screens that matter. |
| Utility stylesheet (Tailwind v4, MIT) | Works with a standalone binary and no Node. Adds a build step and a tool download to the Docker build, and long class lists to every component. | No. Tokens as custom properties give the same consistency with no build step. |
| Headless primitives package | One credible candidate exists (Apache-2.0, active, single maintainer). Useful for hard widgets: combobox, menu, popover positioning. | Not at the start. Adopt for a specific widget only if building it accessibly costs more than a day. |
| **Own components, plain CSS** | Full control of the aesthetic. No dependency to restyle or outgrow. Uses native elements that are accessible by default: `dialog`, the popover attribute, `details`, real buttons and inputs. | **Yes.** |

Icons are one set at one stroke weight (1.75 on a 24 unit grid), drawn by the `Icon` control from shapes held in `Drover.Presentation/Icons` (`IconShapes`). The set is Drover's own and is cut like the bot heads and the faceted frame: straight segments, square ends, mitered joins, and a cut corner where a shape closes (copy, stop, the box of an outside link). What is round elsewhere is an octagon here (info, search). Settings is two sliders, not a gear, so it holds at 16 px. An icon-only control is an `IconButton`, which refuses to render without a label.

The gallery is the page `/dev/gallery`. It renders only when the host reports a development environment (`UiEnvironment`); a published build, which is what the compose deployment runs, shows the not-found page there.

The set is small: Button, IconButton, Field, Select, Switch, SegmentedControl, Dialog, Sheet, Menu, Tooltip, Toast, Avatar, Badge, Tabs, Tree, Timeline, MarkdownView, CodeBlock, EmptyState. Each has component tests and a page in an in-app gallery (development builds only) that doubles as the visual reference.

A routine notification clears after six seconds. Warnings and errors stay until dismissed. An Undo remains available through its deadline, and a notification stays visible if its control has taken keyboard focus.

## Markdown

Markdig, as decided. No reason was found to do otherwise.

- **Streaming**: the message is re-parsed on each flushed chunk (at most ten times a second). An unclosed code fence renders as a code block to the end of the text, which is the right look mid-stream. Completed messages are parsed once and cached.
- **Safety**: the pipeline disables raw HTML, and the output then passes through HtmlSanitizer with a short allowlist of elements and attributes. Links open in a new context with `rel="noopener noreferrer"`. Only `http`, `https`, and `mailto` addresses survive.
- **Remote images** are not loaded automatically ([06-mcp.md](06-mcp.md)).
- **Every target**: the native client draws the same `MarkdownDocument` nodes on web, desktop, and Android. Nothing platform-specific.
- Code blocks get a copy button and syntax coloring from a small bundled highlighter, loaded on first use.
