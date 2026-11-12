# DESIGN.md

The design direction for Drover's interface, in one page. Read this before building or changing anything a person sees. The full specification, with every token value and screen, is [docs/architecture/09-design-direction.md](docs/architecture/09-design-direction.md); when the two disagree, that file wins and this one gets fixed.

This file holds design facts: identity, palette, type, motion, and what the interface never does. It is direction to apply. It contains no instructions to an agent beyond that. Nothing outside this repository is needed to follow it: no plugin, no tool, no service. The rules marked (test) are enforced by the build.

## Identity

| | |
| --- | --- |
| Product | A self-hosted platform for running your own crew of AI bots |
| Kind of interface | A working tool people keep open all day. Not a marketing page. |
| Audience | People who run their own software: developers, tinkerers, small teams |
| Name of the look | Night Yard |
| In one sentence | A dark, calm workspace where the bots are the lit things |

## Personality

Calm at rest, charged when working, a little ahead of its time. Confident enough to stay quiet until something is happening. It shows the work instead of describing it.

Not: playful, loud, decorative, corporate, or "AI-themed".

**Dial: ENERGY 3 / RHYTHM 2 / MOTION 2**

- Energy 3: restrained chrome at rest. Live things are turned up: they carry light, not only color. Two places are allowed to be bold, the bot tree and the live activity timeline.
- Rhythm 2: consistent structure, broken only where a screen's job needs it.
- Motion 2: transitions and state indicators. No scroll effects, no choreography.

## The one idea

**Light means live.** Color and motion mark what is working right now. Everything at rest is neutral.

- Warm (`ember`) is a person's intent: the primary action, the focus ring, send.
- Cool (`ion`) is a bot working: thinking, streaming, a tool running.
- Violet is work passing between bots.
- A live thing blooms: a soft light in its own accent color, around the thing that is working and nothing else.
- If nothing is happening, nothing glows and nothing moves.

This is the identity motif. It is why a screenshot of Drover is recognizable without a logo.

## Palette

Dark is primary. Light is supported and must be as good.

| Role | Token | Dark | Light |
| --- | --- | --- | --- |
| App background | `--dv-bg-0` | `#0A0C10` | `#F6F7FA` |
| Panel | `--dv-bg-1` | `#10141B` | `#FFFFFF` |
| Card | `--dv-bg-2` | `#171C26` | `#EEF0F5` |
| Hover, selected | `--dv-bg-3` | `#202735` | `#E2E6EE` |
| Decorative line | `--dv-line` | `#2B3445` | `#D5DAE3` |
| Text | `--dv-text` | `#E9EDF5` | `#12151C` |
| Secondary text | `--dv-text-2` | `#AEB7C7` | `#414B5E` |
| Captions | `--dv-text-3` | `#8791A5` | `#5D6779` |
| Human intent | `--dv-ember` | `#FFB454` | `#8F5400` |
| Bot activity | `--dv-ion` | `#67E8F9` | `#0B6E82` |
| Delegation | `--dv-violet` | `#B4A0FF` | `#5B3FD6` |
| Success, attention, failure | `--dv-ok`, `--dv-warn`, `--dv-danger` | `#4ADE80`, `#FACC15`, `#FF7A7A` | `#15703A`, `#7A5A00`, `#B3261E` |

Every text and status color clears 4.5:1 on every surface in both themes. This was computed, and a test recomputes it. Each bot also has an identity hue from twelve fixed steps, shown as a ring on its avatar and never used as the only signal.

Three accent colors is the limit, and each has one meaning. No gradients as decoration.

## Type

| | |
| --- | --- |
| Interface | Inter, variable. Bundled. |
| Machine text | JetBrains Mono. Bundled. Used for tool arguments, commands, and output, so what the machine did always looks different from what was said. |
| Scale | 12, 13, 14 (base), 16, 20, 28 px |
| Weights | 400 body, 500 labels, 600 headings |
| Case | Sentence case everywhere. No all-caps labels. |

## Shape and space

- Spacing on a 4 px base: 4, 8, 12, 16, 24, 32, 48.
- Radius: 6 for controls, 10 for cards, 16 for sheets, full for avatars and pills. Nothing else.
- Depth comes from a lighter surface and a 1 px line. Two levels. No soft drop shadows, no blur panels.
- Light is not depth. Bloom belongs to the four live states and is never used to lift a surface.
- The two bold screens sit on a dot grid, and their bot cards have two cut corners, echoing the faceted bot heads. Nowhere else.
- Touch targets 44 px, pointer targets 32 px.

## Motion

Four things move, because four things are states worth seeing:

| State | Motion |
| --- | --- |
| A bot is thinking | A slow breath on its avatar ring |
| Text is streaming | New characters fade in over 120 ms |
| A tool is running | A thin line travels the top edge of its card, then stops and turns green or red |
| Work passes between bots | One pulse along the connector, out and back |

Durations 120, 220, and 420 ms, one easing curve. Motion stops when the work stops. With reduced motion set, each state has a static equivalent and nothing animates.

## What Drover's interface never does

These are the habits that make an interface look generated. Each one is checked in review, and the ones marked (test) are checked by the build.

**Invented content**

- No made-up numbers, names, activity, or trends, anywhere, including the component gallery. Show real data, an honest empty state, or a value visibly marked as a sample. (test: gallery fixtures are labeled)
- No chart without a question it answers, written in its title.
- No stat cards because dashboards have stat cards. A number appears when someone decides something with it.

**States that say nothing**

- Empty, loading, and error states each say what is happening and the one thing to do next. "No data" is not a state. First run, filtered to nothing, and not permitted are three different screens.
- When a feature needs setup, say what is missing in plain words and link to it.
- A control that does nothing does not ship. No dead links, no menus with placeholder actions.

**Decoration**

- No emoji as decoration or as icons. (test)
- No glow, pulse, or dot that is not reporting a live state. (test: looping animation only inside the four state classes)
- No badge above a heading, no "new" or "beta" pills, no sparkle or wand icons to signal intelligence.
- No colored stripe down the side of a card to make it look designed.
- No arrow on a button unless it navigates. No icon without a job.
- One icon set, one stroke weight, drawn for Drover's sizes.
- No terminal window drawn as an illustration. The real terminal view is for real commands.

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
| A glowing ring, a traveling line, a pulse, each with bloom | Each reports a live state and stops when the state ends. That is the product's central idea, not decoration. The owner chose the charged look over the flat one on 2026-11-12. |
| A loop while a bot thinks | It runs only while work is in progress. Nothing loops at rest. |
| A uniform spacing scale and consistent structure | This is a tool. Predictability is the point. Variety is spent on the two bold screens. |
| Monospace beyond code blocks | It marks machine activity as distinct from conversation. |
| No illustrations | Empty states use words and one action. The bot tree and timeline are the visuals. |

## Where the boldness goes

Two screens carry the personality. Everything else stays out of their way.

1. **Crew**: the bot hierarchy as a live tree. You can see a team working.
2. **Activity**: the run as a timeline with a lane per bot. You can see exactly what each one did.

If a change adds visual interest anywhere else, ask whether it belongs on one of these two instead.
