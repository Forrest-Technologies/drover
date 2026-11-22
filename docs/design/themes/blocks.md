# The blocks group, proposed for owner approval

Issue 783 adds a `blocks` group to the theme format, so code and output blocks, tables, diffs, logs, card bodies and
native file previews follow the theme. This page is the design posted for approval before any code. The values sit in
`scripts/themes/blocks.json` until they are approved; then they move into the theme files and the format table in
`docs/architecture/09-design-direction.md`.

Pictures: `docs/screenshots/themes/blocks/`, drawn by `python3 scripts/themes/blocks-sheet.py`, which also checks
every tone below. Every word in them is a sample.

## The group

| Key | Type | Bounds and meaning |
| --- | --- | --- |
| `blocks.surface` | `bg-1` or `bg-2` | The surface a block stands on |
| `blocks.line` | integer | 1 or 2: the block's edge and the line under its header |
| `blocks.padding` | integer | 2 to 4: a step of the spacing scale (8, 12 or 16 px before density) |
| `blocks.machineSize` | integer | 12 to 14 px: machine text in blocks |
| `blocks.outputLines` | integer | 8 to 24: lines a block shows inside a card before it scrolls (`DvOutputLines`) |
| `blocks.wrap` | boolean | Whether code wraps until the person turns wrapping off. Today it never does |
| `blocks.highlighting` | `weight` or `quiet` | `weight`: today's treatment, theme text tokens only (keywords at heading weight, keys at label weight, quoted text and punctuation in secondary text, comments in caption text). `quiet`: the same, with quoted text and comments in two tones of their own |
| `{mode}.color.code-quoted` | color | `quiet` only. 4.5:1 on `bg-1` and `bg-2`; OKLCH chroma at most 0.08; hue at least 45 degrees from `ember`, `ion` and `violet`, and at least 30 from `danger` |
| `{mode}.color.code-comment` | color | `quiet` only. 4.5:1 on `bg-1` and `bg-2`; a near neutral, chroma at most 0.03, so it can never read as an accent |

A theme with `weight` highlighting has neither color. Diffs and logs keep the status colors and their `+`, `-` and
level words in every theme; they are never highlighted. Bot-written HTML is never themed: only its frame, header and
refusal states follow the theme.

## Per theme

| Theme | Surface | Line | Padding | Machine text | Lines in a card | Wrap | Highlighting | Why |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Night Yard | `bg-2` | 1 | 3 | 13 | 12 | off | weight | Today's block, exactly |
| Signal Room | `bg-2` | 1 | 2 | 12 | 16 | off | weight | Dense: more lines, less padding, no extra color |
| Paper Ledger | `bg-2` | 1 | 4 | 14 | 12 | on | quiet | Reading: room around text, wrapping like prose, soft ink tones |
| Workbench | `bg-2` | 2 | 3 | 14 | 10 | on | weight | Touch and low vision: large text, no sideways scrolling on a phone, 7:1 text kept |
| Blueprint | `bg-1` | 1 | 3 | 13 | 16 | off | quiet | Drawing sheet: blocks sit on the panel level, lines carry structure |
| Quiet Hours | `bg-2` | 1 | 3 | 13 | 12 | off | weight | Low stimulus: no extra color |
| Foundry | `bg-2` | 1 | 3 | 13 | 12 | off | quiet | Warm: sage quoted text between brass and teal |

## Quiet tones, computed

| Theme | Mode | `code-quoted` | Lowest | From accents | From `danger` | `code-comment` | Lowest | Chroma |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Paper Ledger | light | `#556F45` | 4.55 | 75 | 104 | `#6E6757` | 4.56 | 0.026 |
| Paper Ledger | dark | `#7E996D` | 5.31 | 65 | 105 | `#998D7F` | 5.15 | 0.025 |
| Blueprint | dark | `#7A9A70` | 5.01 | 70 | 118 | `#86909F` | 4.87 | 0.025 |
| Blueprint | light | `#51734E` | 4.60 | 79 | 114 | `#616B79` | 4.63 | 0.025 |
| Foundry | dark | `#80986B` | 5.56 | 50 | 101 | `#9B8C80` | 5.42 | 0.025 |
| Foundry | light | `#546A3F` | 4.60 | 53 | 99 | `#6C6355` | 4.54 | 0.024 |

Quoted text in all three lands on a muted green, the one family far from every accent in these themes. It is near
the hue of `ok`, at about half its chroma; success is shown only in diffs, logs and status words, which are never
highlighted, so the two do not meet.
