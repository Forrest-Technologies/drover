# DESIGN.md

The design direction for Drover's interface, in one page. Read this before building or changing anything a person sees. The full specification, with every token value and screen, is [docs/architecture/09-design-direction.md](docs/architecture/09-design-direction.md); when the two disagree, that file wins and this one gets fixed.

This file holds design facts: identity, palette, type, motion, and what the interface never does. It is direction to apply. It contains no instructions to an agent beyond that. Nothing outside this repository is needed to follow it: no plugin, no tool, no service. The rules marked (test) are enforced by the build.

## Identity

| | |
| --- | --- |
| Product | A self-hosted platform for running your own crew of AI bots |
| Kind of interface | A working tool people keep open all day. Not a marketing page. |
| Audience | People who run their own software: developers, tinkerers, small teams |
| Name of the look | Front Office |
| In one sentence | A neutral, quiet workspace where only what is working carries light |

## Themes

Front Office is the default look, a professional one ([ADR 0021](docs/adr/0021-front-office-black-and-accents.md)), and it is one theme of a family ([ADR 0020](docs/adr/0020-a-family-of-themes.md)). Night Yard, the first look, charged and faceted, stays in the app. Each theme has a dark and a light mode, and a black mode worked out from the dark one for screens that turn their pixels off, and may set color, type, density, shape, surfaces, motion and how the characters are drawn, inside the bounds in [09-design-direction.md](docs/architecture/09-design-direction.md#themes). No theme changes what this page holds: the three accent roles, light means live, the four live states and their static equivalents, depth by surface and line only, and everything under "What Drover's interface never does". The designed themes are Front Office, Night Yard, Signal Room, Paper Ledger, Workbench, Blueprint, Quiet Hours and Foundry. The rest of this page describes Front Office, and says where Night Yard differs.

## Personality

Calm at rest, lit when working, a little ahead of its time. Confident enough to stay quiet until something is happening. It shows the work instead of describing it, and it shows a control when it is needed, not before.

Not: playful, loud, decorative, stiff, or "AI-themed".

**Dial: ENERGY 2 / RHYTHM 2 / MOTION 2** (Night Yard: energy 3)

- Energy 2: restrained chrome at rest. Live things carry light, not only color, and less of it than in Night Yard. Two places are allowed to be bold, the bot tree and the live activity timeline.
- Rhythm 2: consistent structure, broken only where a screen's job needs it.
- Motion 2: transitions and state indicators. No scroll effects, no choreography.

## The one idea

**Light means live.** Color and motion mark what is working right now. Everything at rest is neutral.

- Warm (`ember`) is a person's intent: the primary action, the focus ring, send, and the tint of their own messages. It is the one color a person may turn: to an accent the theme offers, never to one near the other two.
- Cool (`ion`) is a bot working: thinking and streaming. The line that travels around a working frame is the one exception: it takes the theme's own color, `ember`, so it matches the accent a person chose.
- Violet is work passing between bots.
- A live thing blooms: a soft light in its own accent color, around the thing that is working and nothing else.
- If nothing is happening, nothing glows and nothing moves.

This is the identity motif. It is why a screenshot of Drover is recognizable without a logo.

## Palette

Dark is primary: the app is dark until a person chooses light or asks it to follow their device. Light is supported and must be as good.

| Role | Token | Dark | Light |
| --- | --- | --- | --- |
| App background | `--dv-bg-0` | `#0B0C0E` | `#F6F6F7` |
| Panel | `--dv-bg-1` | `#131417` | `#FFFFFF` |
| Card, a bot's bubble | `--dv-bg-2` | `#1B1D21` | `#EEEEF0` |
| Hover, selected | `--dv-bg-3` | `#25282D` | `#E2E3E6` |
| Decorative line | `--dv-line` | `#2F333A` | `#D3D5DA` |
| Text | `--dv-text` | `#EDEEF0` | `#131518` |
| Secondary text | `--dv-text-2` | `#B6BAC2` | `#454A53` |
| Captions | `--dv-text-3` | `#8C9198` | `#5F636A` |
| Human intent | `--dv-ember` | `#F08D5F` | `#A24A1C` |
| A person's bubble | `bubble`, worked out from `ember`, not in the token files | `#4D352D` | `#E6C8BB` |
| Bot activity | `--dv-ion` | `#6BD8DE` | `#006E73` |
| Delegation | `--dv-violet` | `#D398E0` | `#8A3D9A` |
| Success, attention, failure | `--dv-ok`, `--dv-warn`, `--dv-danger` | `#6AD18A`, `#E2C157`, `#F47B76` | `#007338`, `#786101`, `#A83636` |

These are Front Office's values, from its theme file. The token files `tokens.css` and `Tokens.xaml` hold Night Yard's, and the theme in use is written over them before the first frame. The black mode puts the app background at `#000000` and moves each surface one level down. Every text and status color clears 4.5:1 on every surface in every mode, and text clears it on a person's bubble. This is computed for every theme, and a test recomputes it. Each bot also has an identity hue from twelve fixed steps. Its character's head carries the hue, and it is never the only signal.

Three accent colors is the limit, and each has one meaning. No gradients as decoration.

## Type

| | |
| --- | --- |
| Interface | Inter, variable. Bundled. |
| Machine text | JetBrains Mono. Bundled. Used for tool arguments, commands, and output, so what the machine did always looks different from what was said. |
| Scale | 12, 13, 15 (base), 17, 20, 28 px. Night Yard: 12, 13, 14, 16, 20, 28 |
| Weights | 400 body, 500 labels, 600 headings |
| Case | Sentence case everywhere. No all-caps labels. |

## Shape and space

- Spacing on a 4 px base: 4, 8, 12, 16, 24, 32, 48.
- The frame is round: controls are pills or nearly so, and cards, message bubbles and the composer round all four corners by 18 px, sheets and dialogs by 24. One class, `Facet`, draws every frame in the corner language of the theme in use. In Night Yard the frame is faceted: two cut corners (top left, bottom right), 6 px on controls and 10 px on larger frames, echoing the bot heads.
- Depth comes from a lighter surface and a 1 px line. Two levels. No soft drop shadows, no blur panels.
- Light is not depth. Bloom belongs to the four live states and is never used to lift a surface.
- Surfaces are plain. In Night Yard the working area (the conversation, the setup screens, the two bold screens) sits on a faint dot grid, the yard.
- Icons are cut in every theme: Drover's own set, straight segments with mitered joins, a cut corner where a shape closes. No rounded strokes.
- Touch targets 44 px, pointer targets 32 px.

## Bots are characters

Every bot is a head with a visor: one band of light where a face would be. The visor is dark at rest and lit while the bot works, which is "light means live" made into a character.

- Built from parts stored as data: six heads, six visors, six tops, six marks, a tilt, and the hue. The same bot always gets the same character.
- The visor is the state: dark at rest, a slow breath when thinking, steady when writing, a bright bar sweeping across when a tool runs, a pale red when failed. Each state is also said in words.
- Below 24 px the top and the mark drop out; the head and visor remain.
- In a conversation the bot speaks from its character. Both sides speak in bubbles: a person's on the right in a tint of their intent, a bot's on the left on the card surface, under its head at 24 px and its name, and the head stands at 48 px in the conversation's header. In Night Yard a bot's words run the full width as a document beside its head at 48 px on a faceted plate, whose edge takes the live color only while that bot is thinking or writing.
- The crew is how a person gets around. The first column is the bot hierarchy, each bot under the one that manages it, and choosing a bot opens its one main conversation. A person talks to a bot, not to a thread. A side chat with the same bot is the exception: it opens beside the main conversation and never replaces it. The conversation's running summary opens in that same place.
- The Drover mark is the lead of the crew: the wedge head, cocked, with the visor cut out so it holds in one color. While the app starts, the mark stands alone on the first surface: no word, no spinner.
- Nothing is a soft blob with eye marks, and nothing resembles another product's avatars.

## Motion

Four things move, because four things are states worth seeing:

| State | Motion |
| --- | --- |
| A bot is thinking | A slow breath of light across its visor |
| Text is streaming | New characters fade in over 120 ms |
| A tool is running | A thin line in the theme's own color travels around the outline of its card, following the card's corners, then stops and the card's edge turns green or red. While a bot works, the same line travels around the composer ([ADR 0022](docs/adr/0022-live-line-goes-around-the-frame-in-the-accent.md)) |
| Work passes between bots | One pulse along the connector, out and back |

Durations 120, 220, and 420 ms, one easing curve. Motion stops when the work stops. With reduced motion set, each state has a static equivalent and nothing animates.

## What Drover's interface never does

These are the habits that make an interface look generated. Each one is checked in review, and the ones marked (test) are checked by the build.

**Invented content**

- No made-up numbers, names, activity, or trends, anywhere, including the component gallery. Show real data, an honest empty state, or a value visibly marked as a sample. (test: gallery fixtures are labeled)
- No chart without a question it answers, written in its title.
- No stat cards because dashboards have stat cards. A number appears when someone decides something with it.

**States that say nothing**

- Empty, loading, and error states each say what is happening and the one thing to do next. "No data" is not a state. First run, filtered to nothing, and not permitted are three different screens. The start of the app is the one exception: the mark alone, named for assistive technology. (test: filler such as "no data", "coming soon", or an apology in interface text)
- When a feature needs setup, say what is missing in plain words and link to it.
- A control that does nothing does not ship. No dead links, no menus with placeholder actions. (test: a link with no address)

**Decoration**

- No emoji as decoration or as icons. (test)
- No glow, pulse, or dot that is not reporting a live state. (test: looping animation only inside the four state classes)
- No badge above a heading, no "new" or "beta" pills, no sparkle or wand icons to signal intelligence.
- No colored stripe down the side of a card to make it look designed.
- No arrow on a button unless it navigates. No icon without a job.
- One icon set, one stroke weight, drawn for Drover's sizes.
- No terminal window drawn as an illustration. The real terminal view is for real commands.

**Things that stay when they are not needed**

- A control shows when a person needs it. A reply's actions (copy, another reply, its versions, what happened) show on the newest reply, and on an older one when it has the focus or is tapped. Ways into a conversation's panels are in one menu, and the side chats, where a person goes most, also have an icon button beside it. Hover reveals nothing, so a finger and a keyboard reach everything a pointer does.
- What a reply used is said once, in the status line under the composer, with the model that wrote it. It is not repeated under every reply.
- The status line says something or is not there. No placeholder, no zero.

**Sameness**

- No default shell chosen before the screen's job is named. Each screen states the one thing the person does there, and its layout serves that.
- Tables show the columns that decide the person's next move, with the deciding one first. Not Name, Status, Date, Actions by reflex.
- No three-of-everything. The number of items comes from the content.

**Layout that breaks**

- Phone first. Every screen works at 360 px wide with no sideways scroll. (test)
- Nothing depends on hover. Every hover action has a tap and a keyboard path.
- No `100vh`; use the dynamic viewport unit so mobile browser bars do not cut content. (test)
- Text is sized so it zooms. The page works at 200 percent.
- Images and embeds have dimensions, so nothing jumps while loading. (test)

**Controls that are not controls**

- A button is a `button`, a link is an `a`, a field has a `label`. No clickable `div`. (test)
- Focus is always visible: a 2 px ember ring with 2 px offset. Never removed. (test)
- An icon-only control has an accessible name. (test)
- State is never shown by color alone.

**Words**

- Plain words, sentence case, short. Say what the thing does.
- No hype: no "powerful", "seamless", "supercharge", "magic", "next-gen". No exclamation marks.
- No "AI-powered" anywhere. The whole product is bots; saying so is noise.
- Errors say what failed and what to do. No apology, no blame, no code without a sentence.
- Buttons name the action: "Connect calendar", not "Submit" or "Get started".
- Never name or compare against another product.

## Choices a generic checklist might question, made on purpose

| Choice | Why |
| --- | --- |
| Dark by default | Owner's decision. A tool kept open all day, used beside editors and terminals. Light is fully supported. |
| A glowing ring, a traveling line, a pulse, each with bloom | Each reports a live state and stops when the state ends. That is the product's central idea, not decoration. The owner chose the charged look over the flat one on 2026-11-12, and a restrained default with less bloom on 2026-11-24; the charged look is Night Yard. |
| A person's messages in a tint of their accent | The color is the person's intent, used for what the person said. Text on it is checked at 4.5:1 for every theme, mode and accent. |
| A choice of accent | It turns one color, the person's own, and only to hues that stay apart from a bot working and work passing between bots. |
| A loop while a bot thinks | It runs only while work is in progress. Nothing loops at rest. |
| A uniform spacing scale and consistent structure | This is a tool. Predictability is the point. Variety is spent on the two bold screens. |
| Monospace beyond code blocks | It marks machine activity as distinct from conversation. |
| No illustrations | Empty states use words and one action. The bot tree and timeline are the visuals. |

## Where the boldness goes

Two screens carry the personality. Everything else stays out of their way.

1. **Crew**: the bot hierarchy as a live tree. You can see a team working.
2. **Activity**: the run as a timeline with a lane per bot. You can see exactly what each one did.

If a change adds visual interest anywhere else, ask whether it belongs on one of these two instead.
