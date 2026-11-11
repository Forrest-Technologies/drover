# 0008. Drover's own UI components on plain CSS tokens

- Status: Accepted
- Date: 2026-11-11
- Source: [09-design-direction.md](../architecture/09-design-direction.md)

## Context

The interface has a strong look of its own and two screens, the bot tree and the activity timeline, that no component library provides.

## Decision

A small component set in `Drover.Ui`, styled with CSS custom properties and CSS isolation, using native elements that are accessible by default. No component library and no CSS build tool.

## Alternatives considered

- A styled component library. Rejected: its look fights the design on the screens that matter.
- A utility stylesheet. Rejected: a build step and long class lists for no gain over tokens.

## Consequences

About twenty components to build and test, including accessible menus and dialogs.

## Reopen when

The set is not solid by the end of the first UI step; adopt headless primitives for the hard widgets.
