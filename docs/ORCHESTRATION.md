# Orchestration handoff: Phase 0

For whoever coordinates the coding agents that build Phase 0, human or agent. It tells you what state the repository is in, what to dispatch and in what order, what each worker must be given, and when to stop and ask the owner.

You do not design here. The design is done and lives in `docs/architecture`. Your job is to get the issues finished in the right order without the work drifting from it.

## Goal

The owner chats with a bot that uses MCP tools, through a compose deployment, every day, in place of what they used before. That is the end of Phase 0 and of this handoff.

## State of the repository

| Thing | State |
| --- | --- |
| Design | Written. `docs/architecture/README.md` is the index. Decisions are in `docs/adr`. |
| Solution | `Drover.slnx` builds with zero warnings. 14 source projects, empty, in the dependency direction a test enforces. |
| Server | Runs. Serves the web client and health checks on port 8080. Nothing else yet. |
| CI | Live on hosted runners: build, format, tests, vulnerable packages, license check, CodeQL, secret scan, workflow lint. |
| Rules | `AGENTS.md`, `CODE_STYLE.md`, `DESIGN.md`. All binding. |
| Work | 19 open issues under tracking issue #1. Step 1 (#2) closes when its pull request merges. |

## Before you dispatch anything

1. The step 1 pull request is merged into `main`.
2. `main` is green in CI.
3. The owner has set the repository settings listed on #2, and Dependency graph and Secret scanning are enabled (`docs/architecture/10-tooling.md`).

If any of these is not true, stop and tell the owner. Do not start workers on a branch other than `main`.

## What to read

Read these yourself, once, in this order. About twenty minutes.

1. `AGENTS.md`: the rules every worker must follow
2. `docs/architecture/README.md`: the shape in one page, and the five decisions that carry the weight
3. `docs/architecture/11-phase-0-plan.md`: the steps, the lanes, the milestones
4. Tracking issue #1

Workers read `AGENTS.md` in full, the files their issue links, and `DESIGN.md` for anything a person sees.

## Dispatch plan

Steps are numbered in dependency order but are not one queue.

### Wave 1: right after step 1 merges

Four workers at once. None touches another's files except the shared ones listed under "Where workers collide".

| Lane | Issue | Step | Notes |
| --- | --- | --- | --- |
| A. Data | #4 | 3. Database roles and the tenant scope | The critical path. Start it first and never let it wait. |
| B. Tooling | #3 | 2. Analyzers | When it merges, later code gets stricter checks. Merge it early. |
| C. Deployment | #21 | 1b. Compose walking skeleton | Reaches the "stood up" milestone |
| D. Interface kit | #22 | 11a. Tokens, components, markdown, rule tests | Needs no backend |

### Lane A continues, one at a time

`#4` → `#5` (isolation suite) → `#6` (core schema) → `#7` (sign-in, vault) → `#20` (deployment policy)

### Wave 2: after #6 merges

| Lane | Issue | Step | Notes |
| --- | --- | --- | --- |
| E. Agent runtime | #8 | 7. Agent framework spike | A gate. See below. |
| F. Model providers | #9 | 8. Venice and compatible endpoints | Needs #7 for the vault; the adapters and their conformance suite can start once #6 is in |

### Single file from here

`#10` (run engine; needs #7, #8, #9) → `#11` (streaming) → `#13` (UI shell and chat) → `#12` (bot editor) → `#14` (MCP) → `#15` (activity view) → `#16` (compose hardening, status page) → `#17` (daily-driver pass, release)

Issue numbers #12 and #13 are out of step order. The order above is correct.

### Milestones to report to the owner

| Milestone | When | Tell the owner |
| --- | --- | --- |
| Stood up | #21 merged | `docker compose up --build` works. Give the measured build time and image size. |
| First words | #13 merged | They can chat with a bot through Venice in the browser |
| Useful | #14 merged | The bot uses MCP tools with approval |
| Daily driver | #17 | They confirm on the issue; then tag the release |

## Gates

Hard stops. Do not route around them.

| Gate | Rule |
| --- | --- |
| **Agent framework proofs** | #8 must show its five proofs passing before #10 starts. If a proof fails, the worker reports on #8 with what it observed and a proposed fallback. You take that to the owner. You do not pick the fallback. |
| **Isolation suite** | No table reaches `main` without being covered by the suite from #5. If a worker says the suite is in the way, the worker is wrong. |
| **Build budget** | The image must build inside 2 GB of memory (#21). If it cannot, stop and report the measured peak. Do not raise the limit. |
| **Green before merge** | Every check green. A red check is fixed at its cause. Never skipped, disabled, or marked expected. |

## What to give each worker

A worker gets one issue and nothing it has to guess. Use this as the brief:

```
You are working on Forrest-Technologies/drover, issue #<n>: <title>.

Read first, in full: AGENTS.md. Then the issue, then every file it links.
For anything a person sees, read DESIGN.md.

Do exactly what the issue's "In scope" says. Do not do what "Out of scope" says.
If the issue is unclear or wrong, say so on the issue and stop. Do not guess.

Branch from the latest main. One pull request, titled in Conventional Commits
form, whose body starts with "Closes #<n>" and follows the pull request template.
Say how each acceptance criterion was verified, with the test name or output.

Before you open the pull request, all of these pass locally:
  dotnet build Drover.slnx -warnaserror
  dotnet format Drover.slnx --verify-no-changes
  dotnet test --solution Drover.slnx

No Docker in your environment? For data tests run:
  eval "$(scripts/dev-postgres.sh start)"
Compose work is verified by CI; say so in the pull request.

Stop and report, without working around it, if: a test cannot run in your
environment, an acceptance criterion cannot be met, you need a package whose
license is not on the allowed list, you need a new sys function, or the design
in docs/architecture looks wrong for what you found.
```

## Reviewing a worker's pull request

Check these before it is put in front of the owner. Most are mechanical; the build catches the rest.

1. The body starts with `Closes #n` and addresses every acceptance criterion with evidence, not a claim.
2. It does one issue's work. Extra scope goes back out into a new issue.
3. No test was deleted, skipped, or loosened. No rule was suppressed without a comment giving the reason.
4. New table: ownership class, `IsolateBySpace`, seeder. New package: license row in `docs/architecture/05-dependencies.md`. New `sys` function: manifest entry and a reason.
5. No query outside `Drover.Data`. No framework type outside `Drover.Agents` and server wiring. No vendor type outside its adapter.
6. Interface change: screenshots in dark, light, and 360 px wide, and nothing from the "never does" list in `DESIGN.md`.
7. `docs/architecture` still describes what the code does. If the change made a section wrong, the section is fixed in the same pull request.
8. A changed decision has an ADR that supersedes the old one.

Have a second agent review anything in lane A, #8, #10, and #14. Those are where a mistake is expensive.

## Where workers collide

Several lanes edit the same few files. Expect conflicts here and plan merges around them.

| File | Who touches it |
| --- | --- |
| `Directory.Packages.props` | Almost every issue adds a package |
| `Drover.slnx` | Every issue that adds a project |
| `tests/Drover.Domain.Tests/SolutionLayoutTests.cs` | Every issue that adds a source project must add it to the table |
| `docs/architecture/05-dependencies.md` | Every issue that adds a package |
| `.github/workflows/*.yml` | #21, later #16 |
| `tools/Drover.Analyzers` | #3, then #5 (DRV0006), then #8 (DRV0007) |

Rules that keep this cheap:

- Merge one pull request at a time. After each merge, the open ones rebase on `main` and rerun their checks.
- Merge lane A first when two are ready. Everything else waits on it sooner or later.
- When #3 merges, open pull requests will meet new analyzer rules. That is expected; they fix their code, not the rules.

## Who merges

`main` is protected. The owner merges, or tells you that you may. Until told otherwise, a finished pull request is one that is green, reviewed against the list above, and handed to the owner with a two-line summary: what it does, and anything they should look at.

## When to stop and ask the owner

- A gate is hit.
- A worker reports the design is wrong, or two parts of it disagree.
- An issue needs a decision that is not in `docs/adr` or `docs/architecture`.
- Anything that changes cost or risk for a self-hoster: a new required service, a larger image, a new account someone must create.
- A provider's terms look like they forbid what an issue asks for.
- The same issue has failed twice with two different workers. The issue is probably wrong; do not send a third.

Bring a recommendation with the question. Carry on with the lanes that do not depend on the answer.

## What not to do

- Do not start Phase 1 work, however ready it looks. The schema already has room for it.
- Do not re-plan Phase 0. If the order is wrong, say why and ask.
- Do not edit `ROADMAP.md` status, close issues by hand, or tick a criterion you did not verify.
- Do not give a workflow with a `pull_request` trigger a self-hosted runner. The repository is public.
- Do not add a container registry push, a telemetry call, or a third-party host the interface loads from.
- Do not name or compare against another product anywhere in the repository.
- Do not let a worker install or depend on agent tooling that is not in the repository. Everything a worker needs to do good work is in `AGENTS.md`, `CODE_STYLE.md`, `DESIGN.md`, and the build.

## Reporting

After each merge, one short note to the owner: what merged, what is now in progress per lane, what is blocked and on what. At each milestone, the fuller report in the table above. Report failures as plainly as successes, with the output.

## Known open items

| Item | Where | Who |
| --- | --- | --- |
| Whether the compatible client surfaces Venice's reasoning field, and whether every option works in suffix form | #9 answers it | Worker reports on the issue |
| Whether the license tool, the SBOM tool, and the outdated-packages tool all stay within the allowed licenses | Checked for the first two in step 1; the third arrives with #8 | Worker |
| Measured build time, peak memory, and image size | #21 | Worker; you relay to the owner |
| Venice on a hosted service | `docs/architecture/12-risks.md`, risk 12 | Owner, not blocking Phase 0 |
| Contributor agreement or sign-off | Before the first outside pull request | Owner, not blocking Phase 0 |

## After Phase 0

Tracking issues #18 (Phase 1) and #19 (Phase 2) list their items but are not broken down. When #16 is in review, propose the Phase 1 sub-issues to the owner using the Task form, shaped by what Phase 0 taught. Do not create them unasked.
