# 0022. The line of live work goes around its frame, in the person's accent

- Status: Proposed in its pull request
- Date: 2026-11-25
- Source: the owner's ask of 2026-11-25, with a picture of the composer while a bot worked
- Supersedes: [0021](0021-front-office-black-and-accents.md) and DESIGN.md where they say the line of a running tool travels the top edge of its card in `ion`

## Context

While a bot worked, a short `ion` line flew across the top of the composer, and of a tool card while its tool ran. In
Front Office, whose frames are round, a straight line across the top read as a loading bar laid over the frame rather
than part of it. The owner asked for the line to follow the shape of the frame it belongs to, everywhere it appears,
and to take the color they chose for the theme.

## Decision

The line of live work travels around the whole outline of its frame, in the frame's corner language (round, cut,
square or plain), with the same length, speed and bloom it had on the top edge. It is drawn in `ember`, the theme's own
color, which is the accent a person may turn. One control, `TravelLine`, draws it on the same outline `Facet` draws the
frame on. Where it lies: the composer while a bot works, a tool card or a disclosure while its tool runs, the work
ledger's frame while any of its steps runs, and a running tool's node in the timeline.

With reduced motion it lies still around the whole outline. A fading theme fades it up once and leaves it still.

Thinking, streaming and the visor stay `ion`. The still line a tool leaves when it ends stays green or red.

## Alternatives considered

- Keep the line on the top edge and only recolor it. It still reads as a bar across a round frame.
- Keep `ion`. It is the bot-activity color, but the owner asked for the theme's color, and the line sits on the
  person's own composer and cards.
- Stroke the whole frame edge in a pulsing color. A loop with no direction reads as decoration, not progress.

## Consequences

The accent now marks one live state as well as a person's intent. The meaning stays readable because the line moves
and says its state in words beside it ("Running", Stop in place of Send). Contrast of `ember` is already computed on
every surface for every offered accent.

## Reopen when

People read the moving accent line as something they did rather than something a bot is doing.
