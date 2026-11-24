# 0020. The look is a family of themes, with Night Yard as the default

- Status: Proposed in its pull request; accepted once the owner approves the sample sheet on issue 421
- Date: 2026-11-21
- Source: [issue 421](https://github.com/Forrest-Technologies/drover/issues/421), tracking issue [785](https://github.com/Forrest-Technologies/drover/issues/785)

## Context

Drover had one look, Night Yard, in a dark and a light mode. The owner asked for many more looks, and for looks
that differ in more than color: type, density, shape, surfaces, motion and how the bots' characters are drawn. A
recolor would not answer that. At the same time the identity of the interface, "light means live", the three accent
roles and the list of things the interface never does, must hold in every look, or the product stops being
recognizable and stops being accessible.

## Decision

- The look is a family of named **themes**. Each theme has a dark and a light mode and sets seven token groups:
  color, type, density, shape, surfaces, motion and characters. Every value in a group stays inside the bounds in
  [09-design-direction.md](../architecture/09-design-direction.md#themes).
- A theme is **data**: one JSON file per theme in `src/Drover.Presentation/Theme/Themes/`, in the closed format
  that section defines. Nothing about a theme lives in a component.
- The **invariants** are not tokens, so no theme can change them: the three accent roles and their meanings, light
  means live and only the four live states move, both modes shipped and passing, everything in "What Drover's
  interface never does", the 360 px and 200 percent rules, the static equivalents under reduced motion, and the
  character parts. A look that needs to break one is a change to `DESIGN.md` with its own ADR, not a theme.
- **Night Yard stays the default** and the identity used for the mark, screenshots and documentation.
- Faces beyond Inter and JetBrains Mono are bundled under SIL OFL 1.1, at most two more, loaded only when their
  theme is chosen where the platform allows.

## Consequences

- Seven themes ship as the first batch: Night Yard, Signal Room, Paper Ledger, Workbench, Blueprint, Quiet Hours
  and Foundry. Night Yard's file holds today's values exactly.
- Every token group must switch at run time ([issue 422](https://github.com/Forrest-Technologies/drover/issues/422)),
  and every theme must pass the same accessibility gates in the build
  ([issue 423](https://github.com/Forrest-Technologies/drover/issues/423)).
- Because a theme is data in a closed format, a person's own theme can later use the same format and the same
  checker ([issue 778](https://github.com/Forrest-Technologies/drover/issues/778)).
- `tokens.css` and `Tokens.xaml` become outputs of Night Yard's file instead of hand-kept sources (issue 422).
