# Professional presets, drafts for owner approval

Issue 788: Slate, Linen and Graphite, restrained looks in the `professional` group. These are drafts. They sit here, not
in `src/Drover.Presentation/Theme/Themes`, until the owner approves them on issue 788; then they move there and ship.

- `slate.json`, `linen.json`, `graphite.json`: full theme files in the format of
  `docs/architecture/09-design-direction.md` ("Theme files"), `group: "professional"`, every value inside its bounds.
- `sample-sheet.html` (`python3 scripts/themes/sample-sheet.py --folder docs/design/themes/professional --out
  docs/design/themes/professional/sample-sheet.html`): the conversation with a running tool and an approval card, the
  crew and the bot editor, desktop and 360 px, both modes, and twenty-four characters each.
- `blocks-sheet.html` (`python3 scripts/themes/blocks-sheet.py --folder ... --out ...`): code, a shell command, a
  diff, a table and a log line, with today's block and weight-only highlighting.
- Pictures: `docs/screenshots/themes/professional/`. Every word in them is a sample.

What makes them professional: near-gray surfaces (OKLCH chroma at most 0.014), accents at about two thirds of Night
Yard's chroma, Inter only (no new face), standard density, square corners at radius 4, 1 px lines, two surface levels,
no yard, bloom 0 to 2 (an outlined visor at 0), motion 0.9 to 1.0, heads at reduced chroma (0.055 to 0.06).
