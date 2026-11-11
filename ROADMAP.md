# Roadmap

Last updated: 2026-11-11. Rewritten at the end of each phase.

This file gives direction. It is not a schedule and holds no status: the live state of each item is its tracking issue. Design is in [`docs/architecture`](docs/architecture/README.md), decisions are in [`docs/adr`](docs/adr/README.md).

## Where Drover is going

A self-hosted platform for running your own crew of AI bots: fully defined by you, on every device you own, with your own keys.

## Phases

Each phase ends when it is used daily in place of what came before.

### Phase 0: first usable slice · [tracking issue #1](https://github.com/Forrest-Technologies/drover/issues/1)

**Goal:** chat with a bot that uses MCP tools, through a compose deployment, every day.

| Step | Issue |
| --- | --- |
| 1. Repository skeleton, solution, live CI | [#2](https://github.com/Forrest-Technologies/drover/issues/2) |
| 1b. Compose walking skeleton | [#21](https://github.com/Forrest-Technologies/drover/issues/21) |
| 2. Style and data access analyzers | [#3](https://github.com/Forrest-Technologies/drover/issues/3) |
| 3. Database roles and the tenant scope | [#4](https://github.com/Forrest-Technologies/drover/issues/4) |
| 4. Isolation suite | [#5](https://github.com/Forrest-Technologies/drover/issues/5) |
| 5. Core schema | [#6](https://github.com/Forrest-Technologies/drover/issues/6) |
| 6. Sign-in, first run, secret vault | [#7](https://github.com/Forrest-Technologies/drover/issues/7) |
| 6b. Deployment policy presets | [#20](https://github.com/Forrest-Technologies/drover/issues/20) |
| 7. Agent framework spike, kept as tests | [#8](https://github.com/Forrest-Technologies/drover/issues/8) |
| 8. Model providers: Venice and compatible endpoints | [#9](https://github.com/Forrest-Technologies/drover/issues/9) |
| 9. Run engine | [#10](https://github.com/Forrest-Technologies/drover/issues/10) |
| 10. Streaming API and client | [#11](https://github.com/Forrest-Technologies/drover/issues/11) |
| 11. UI shell, setup, streaming chat | [#13](https://github.com/Forrest-Technologies/drover/issues/13) |
| 11a. Interface kit: tokens, components, markdown | [#22](https://github.com/Forrest-Technologies/drover/issues/22) |
| 12. Bot editor and tool grants | [#12](https://github.com/Forrest-Technologies/drover/issues/12) |
| 13. MCP connections and approval | [#14](https://github.com/Forrest-Technologies/drover/issues/14) |
| 14. Tool activity view | [#15](https://github.com/Forrest-Technologies/drover/issues/15) |
| 15. Compose deployment and status page | [#16](https://github.com/Forrest-Technologies/drover/issues/16) |
| 16. Daily-driver pass and v0.1.0 | [#17](https://github.com/Forrest-Technologies/drover/issues/17) |

After step 1, steps 1b, 2, 3, and 11a can be worked at the same time. Steps 7 and 8 can be done in either order. Step 9 waits for step 7's proofs. The lanes are laid out in the [tracking issue](https://github.com/Forrest-Technologies/drover/issues/1).

### Phase 1: daily driver · [tracking issue #18](https://github.com/Forrest-Technologies/drover/issues/18)

**Goal:** a crew of bots that delegate to each other, run code, and reach you with push, on phone and desktop.

Bot hierarchy and delegation, teams, cost roll-up, sandboxes, Android and Windows apps, push, attachments, memory, schedules, web search, ChatGPT plan sign-in for self-hosted servers.

### Phase 2: full product · [tracking issue #19](https://github.com/Forrest-Technologies/drover/issues/19)

**Goal:** a complete product other people can share a space in.

Shared spaces, a Docker sandbox, skill import, voice, more sign-in options, bots reachable from outside, data export, the Apple apps.

## How work is tracked

- A unit of work is a GitHub issue written with the Task form: context, goal, scope, out of scope, acceptance criteria, verification.
- A phase is a tracking issue with sub-issues. Order is expressed with "blocked by".
- One issue, one branch, one pull request that starts with `Closes #n`.
- A decision that would be expensive to reverse is an ADR.
- Later phases are broken into issues when the phase before is nearly done, so what was learned shapes them.
