# 11. Phase 0 build plan

Each step is one sitting, ends with passing tests, and leaves `main` working. The order puts the things that are expensive to retrofit first: tooling, tenancy, and proof that the agent framework's extension points behave as the design needs.

Phase 0 ends when the owner chats with a bot, with MCP tools, through a compose deployment, every day.

**Status is not tracked in this file.** Each step is a GitHub issue under the Phase 0 tracking issue; see [`ROADMAP.md`](../../ROADMAP.md).

## Changes to the phase split

| Change | Reason |
| --- | --- |
| Human approval for `ask` grants is in Phase 0, in its plain form | Phase 0 ships tool grants with an "ask" effect. Without the prompt, "ask" would have to behave as deny or allow, and both are wrong. The framework's approval support makes this small. "Approval prompts refined across clients" stays in Phase 2. |
| Bot hierarchy columns and the closure table ship in the Phase 0 schema, with every bot at the top level | The grant function already takes the minimum along the path to the root. Phase 1 then adds behavior, not a migration that touches every grant query. |
| Venice is a Phase 0 provider, beside the generic compatible endpoint | Owner requirement: first-party support. |
| Per-user isolation inside shared spaces ships in the Phase 0 schema and isolation suite | Owner requirement: row-level security between users is the normal case. The UI for shared spaces is still Phase 2. |

## Fast path: from nothing to stood up

The steps below are numbered in dependency order, but they are not one queue. Three choices get a running deployment into the owner's hands early and keep several coding agents busy at once.

**1. Stood up from the first day.** Step 1b puts a compose deployment of the skeleton server in place right after step 1. From then on `docker compose up --build` always gives a running Drover at whatever stage `main` has reached, CI builds the image under the memory budget on every pull request, and step 15 becomes hardening instead of a first attempt.

**2. Lanes that run in parallel.** After step 1, four lanes do not wait on each other:

| Lane | Steps | Needs |
| --- | --- | --- |
| A. Data and tenancy | 3, 4, 5, then 6 and 6b | Step 1. This is the critical path. |
| B. Tooling | 2 | Step 1 |
| C. Deployment | 1b | Step 1 |
| D. Interface kit | 11a: tokens, base components, markdown view, gallery | Step 1. Built against no backend. |

After step 5, two more open:

| Lane | Steps | Needs |
| --- | --- | --- |
| E. Agent runtime | 7 | Step 5 |
| F. Model providers | 8 | Step 6 for the vault; the adapters and their conformance suite can start after step 1 with the key passed in directly |

Lanes join at step 9 (run engine), which needs 6, 7, and 8. From there the path is single file: 9, 10, 11, 12, 13, 14, 15, 16.

**3. Milestones the owner can feel.**

| Milestone | Reached after | What the owner can do |
| --- | --- | --- |
| Stood up | 1b | Run `docker compose up --build` and open the page |
| First words | 11 | Chat with a bot through Venice, streaming, in the browser |
| Useful | 13 | The bot uses MCP tools, with approval |
| Daily driver | 16 | Stop using the previous tool |

**No Docker in a coding session?** The data tests accept an existing PostgreSQL through `DROVER_TEST_POSTGRES`, and `scripts/dev-postgres.sh` starts one. No lane is blocked on Docker except 1b and 15, which CI verifies.

## Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | **Repository skeleton.** `Drover.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, lock files, license files, empty projects per [01-solution-layout.md](01-solution-layout.md). Move the workflow drafts into `.github/`. Fill the pinned values the drafts leave open. Confirm the license tool works with central package management. Because the repository is public, set the two repository settings in [10-tooling.md](10-tooling.md) (approval for outside contributors' workflow runs; self-hosted runner group restricted to the release workflow) before the workflows are merged. | `dotnet build -warnaserror` and `dotnet format --verify-no-changes` pass on an empty solution. CI is green. |
| 1b | **Compose walking skeleton.** Multi-stage `Dockerfile` and `compose.yaml` for the skeleton server and PostgreSQL, the `init` service that generates secrets, health check, non-root user. Turn on the CI compose job with the 2 GB build budget, and the Dockerfile linter. | `docker compose up --build` on a clean machine serves the page and reports healthy. CI builds the image inside the memory budget and prints build time and image size. |
| 2 | **`Drover.Analyzers`, style rules.** DRV0001 to DRV0005 with tests and code fixes, and DRV0008 (queries stay in `Drover.Data`). | A deliberately non-compliant sample fails on each rule; the compliant sample is clean. |
| 3 | **Database roles and the tenant scope.** `DroverDbContext`, role bootstrap, `sys` schema with the identity functions, `TenantContext`, the two interceptors, `TenantScope`, the `IsolateBySpace` helper with its audience option. Tables: `spaces`, `users` (Identity), `memberships`. Testcontainers fixture. | The pool-of-one test passes: A sees A, B sees B, no tenant sees nothing, a query outside a scope throws. |
| 4 | **Isolation suite and tenancy analyzer.** Catalog tests, generated per-table behavior tests for spaces and for audiences, the manifests, DRV0006. | Adding a table with no policy, no seeder, or no ownership interface fails the build. A child table that does not inherit its parent's audience fails the build. Both demonstrated against scratch tables. |
| 5 | **Core schema.** Every Phase 0 table from [02-data-model.md](02-data-model.md) with composite foreign keys and audience inheritance, the `effective_grants` function, `run_events` partitioning, `agent_sessions`, `sys.claim_runs` and the other first `sys` functions with their manifest. | Isolation suite covers every table. Grant function matches the C# rule on the shared case table. |
| 6 | **Sign-in and first run.** Identity with cookie sign-in, personal space created with the user, membership middleware, setup endpoints and setup code, `ISecretVault` on Data Protection. | API tests: setup works once and then returns 404; a user gets 404 for another user's space; no response type carries a secret value. |
| 6b | **Deployment policy.** `DeploymentPolicy` with the `self_hosted` and `hosted` presets, loaded at start, shown on the status endpoint, and the preset test matrix ([16-deployment-policies.md](16-deployment-policies.md)). Rules that already have something to guard in Phase 0: outbound address checks, setup code, open registration. | Starting with the hosted preset refuses a request to a private address and refuses registration without the operator's say; the self-hosted preset allows both. A rule cannot be loosened by a space setting. |
| 7 | **Agent framework spike, kept as tests.** In `Drover.Agents`: build an agent from a bot definition; `PostgresChatHistoryProvider` with per-service-call persistence; `PostgresAgentSessionStore`; the tool gate as function middleware; approval through the framework's approval-required tools; telemetry on. Prove the five points listed in [14-agent-framework.md](14-agent-framework.md), each as a conformance test against a scripted fake model. DRV0007. | The five proofs pass, including "kill the process while an approval is pending, restart, answer, the tool runs once". Anything that fails is reported with a fallback before step 9 starts. No framework type outside `Drover.Agents` and server wiring. |
| 8 | **Model providers.** `IModelClientProvider`, the capability record, the provider conformance suite. Two adapters: OpenAI-compatible, and Venice with model discovery, capabilities and prices, system prompt off, reasoning mapping, and balance and rate-limit handling. | Both adapters pass the suite against recorded fixtures. A live Venice check passes when run by hand with a key. |
| 9 | **Run engine.** Message to run, the worker with leases, `IRunGovernor`, the event log with the gapless sequence, delta coalescing, cancel, restart recovery including the interrupted-tool rule. Bots, threads, messages API. | Engine tests: a run completes; killing the worker mid-step and starting another finishes the run without repeating a finished model call; cancel stops a run; events are gapless under concurrent writers. |
| 10 | **Streaming API and client.** The run event stream with replay and resume, the user signal stream, `Drover.Client` with the SSE reader and run state reducer. | API test: disconnect at event N, reconnect with the cursor, receive N+1 onward with nothing missing or repeated, including across a server restart. A second user in the same space cannot open a private thread's stream. |
| 11 | **UI shell and chat.** `tokens.css`, the base components, `Drover.Web` host, sign-in, setup screens with the provider choice, thread list, conversation with streaming `MarkdownView`. Contrast test on the tokens. | The owner can chat with a bot in a browser from `dotnet run` of the AppHost. Component tests assert roles and names. Markdown sanitizer tests pass a hostile-input set. |
| 11a | **Interface kit.** `tokens.css`, the base components, `MarkdownView` with the sanitizer, the development gallery, the contrast test. No backend needed. Can start right after step 1; step 11 then only builds screens. | Component tests assert roles and names. The contrast test passes. The sanitizer passes the hostile-input set. |
| 12 | **Bots and grants UI.** Bot editor with the full definition model and progressive disclosure, driven by the model's capability record. Tool grant controls. Built-in tools seeded per space. | Every bot field in the data model is editable in the UI and through the API. A bot with no grant for a tool does not receive that tool; asserted at the runtime boundary. |
| 13 | **MCP connections.** `IToolSource` over the MCP SDK with automatic protocol choice, stateless preferred, and the negotiated mode stored and shown. No-auth and header first, then OAuth with the callback bridge, token cache on the vault, catalog upsert with definition pinning, refresh and reconnect states. The approval prompt in the UI. | Conformance suite against in-process test servers of both protocol generations, for all three auth styles. A changed tool definition flips its grants to review. A per-user credential is unreadable in another user's transaction. |
| 14 | **Tool activity view.** Inline tool cards, the activity rail and timeline, the Activity screen, redaction on the way into events. | Each tool call shows arguments, live output, result, and outcome, live and from history. A seeded secret never appears in stored events or logs. |
| 15 | **Compose deployment.** Dockerfile, `compose.yaml` with the `init` service and volumes, start-up sequence, health endpoints, status page, `ComposeFileTests`. Record real build time, peak memory, and image size in the README. | On a clean machine: clone, `docker compose up`, finish setup in the browser with a Venice key, chat with a bot using an MCP tool. CI compose job is green inside the 2 GB budget. |
| 16 | **Daily-driver pass.** The owner uses it for a few days. Fix what hurts. Write the self-hosting README and the upgrade and backup notes. Tag `v0.1.0` through the release workflow. | The owner has stopped using what they used before. The release has a source archive, an SBOM, and attestations. |

## Sequencing notes

- Steps 1 to 6 produce no visible product. They are the part that cannot be added later without rework, and the part coding agents most need in place before they write features.
- Step 7 is the hinge. The design leans on the framework's storage and approval extension points; this step proves them before anything is built on them. Do not start step 9 with a failing proof.
- Steps 8 and 7 are independent and can be done in either order.
- Step 13 is the largest. If it runs long, ship no-auth and header first and OAuth as a follow-up issue; the owner can use Drover daily with header-auth servers meanwhile.
- From step 11 on, the owner can use the product locally every day, before the compose deployment exists.

## What Phase 0 leaves out, on purpose

Bots creating bots, delegation and teams, sandboxes, native apps, push, attachments, memory, schedules running, search, cost screens, shared spaces in the UI, skills, voice, subscription sign-in. The schema already has room for each, so none needs a redesign.

## Phase 1 and 2, in order

Not planned to the step, but sequenced by dependency and risk:

**Phase 1**

1. Hierarchy and delegation: management tools, `GovernedAgent`, child runs, subtree cancel, loop and budget checks, the Crew screen.
2. Teams: workflow checkpoints on PostgreSQL, the orchestration patterns as data on a parent bot.
3. Usage and cost: records, roll-up, the per-bot and per-subtree views.
4. Bot capabilities from the framework's harness: todo list, plan and act modes, file memory, compaction settings.
5. Sandboxes on E2B: adapter, broker, sharing policies, the shell tool, the terminal view.
6. Android push: the UnifiedPush protocol in C# first, then the embedded distributor fallback, then Web Push end to end.
7. MAUI shell for Android and Windows.
8. Attachments, memory with embeddings, schedules, web search (Venice first).
9. ChatGPT plan sign-in for self-hosted servers. Anthropic key connection.

**Phase 2**

1. Shared spaces in the UI, external sign-in, passkeys.
2. Docker sandbox adapter and stdio MCP servers inside sandboxes.
3. Cross-tree delegation links with "borrowed authority asks".
4. Skill import on the framework's skills support. Voice (Venice first).
5. Other subscription sign-ins where permitted.
6. Bots reachable from outside: A2A, AG-UI, a compatible chat endpoint, chat apps.
7. Per-bot evaluations. Bot export and import.
8. Data export and account deletion.
9. Apple: turn on the targets, build the relay, iOS push.
