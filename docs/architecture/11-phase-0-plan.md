# 11. Phase 0 build plan

Each step is one sitting, ends with passing tests, and leaves `main` working. The order puts the things that are expensive to retrofit first: tooling, tenancy, the run log.

Phase 0 ends when the owner chats with a bot, with MCP tools, through a compose deployment, every day.

## Proposed change to the phase split

One item moves, one is added:

| Change | Reason |
| --- | --- |
| Human approval for `ask` grants is in Phase 0 (step 12), in its plain form | Phase 0 already ships tool grants with an "ask" effect. Without the approval prompt, "ask" would have to behave as deny or allow, and both are wrong. The mechanism is also what delegation reuses in Phase 1, so building it now removes risk from the next phase. "Approval prompts refined across clients" stays in Phase 2. |
| Bot hierarchy columns and the closure table ship in the Phase 0 schema, with every bot at the top level | The grant function already takes the minimum along the path to the root. Shipping the tables now means Phase 1 adds behavior, not a migration that touches every grant query. |

Everything else in the handoff's split stands.

## Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | **Repository skeleton.** `Drover.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, lock files, license files, empty projects per [01-solution-layout.md](01-solution-layout.md). Move the workflow drafts into `.github/`. Fill the two pinned values the drafts leave open. Confirm the license tool works with central package management. | `dotnet build -warnaserror` and `dotnet format --verify-no-changes` pass on an empty solution. CI is green. |
| 2 | **`Drover.Analyzers`, style rules.** DRV0001 to DRV0005 with tests and code fixes. | A deliberately non-compliant sample fails on each rule; the compliant sample is clean. |
| 3 | **Database roles and the tenant scope.** `DroverDbContext`, role bootstrap, `sys` schema with the two identity functions, `TenantContext`, the two interceptors, `TenantScope`, the `IsolateBySpace` helper. Tables: `spaces`, `users` (Identity), `memberships`. Testcontainers fixture. | The pool-of-one test passes: A sees A, B sees B, no tenant sees nothing, a query outside a scope throws. |
| 4 | **Isolation suite and tenancy analyzer.** The catalog tests, the generated per-table behavior tests, the manifests, DRV0006. | Adding a table with no policy, no seeder, or no ownership interface fails the build. Demonstrated by a test that does exactly that against a scratch table. |
| 5 | **Core schema.** Every Phase 0 table from [02-data-model.md](02-data-model.md) with composite foreign keys, the `effective_grants` function, `run_events` partitioning, `sys.claim_runs` and the other first `sys` functions with their manifest. | Isolation suite covers every table. Grant function matches the C# rule on the shared case table. |
| 6 | **Sign-in and first run.** Identity with cookie sign-in, membership middleware, the setup endpoints and setup code, `ISecretVault` on Data Protection. | API tests: setup works once and then returns 404; a member of space A gets 404 for space B's resources; no response type carries a secret value. |
| 7 | **Model connections and the runtime adapter.** `IModelClientProvider` for OpenAI-compatible endpoints, `IAgentRuntime` over the agent framework, the conformance suite against a scripted fake model, DRV0007. | Conformance suite passes: plain reply, allowed tool, asked tool, refused tool, parallel read-only tools, cancel mid-stream. No vendor type outside the adapter. |
| 8 | **Run engine.** Message to run, the worker with leases, the step loop, the event log with the gapless sequence, delta coalescing, cancel, restart recovery. Bots, threads, messages API. | Engine tests: a run completes; killing the worker mid-step and starting another finishes the run with a `step.discarded` event; cancel stops a run; events are gapless under concurrent writers. |
| 9 | **Streaming API and client.** The run event stream with replay and resume, the user signal stream, `Drover.Client` with the SSE reader and run state reducer. | API test: disconnect at event N, reconnect with the cursor, receive N+1 onward with nothing missing or repeated, including across a server restart. |
| 10 | **UI shell and chat.** `tokens.css`, the base components, `Drover.Web` host, sign-in, setup screens, thread list, conversation with streaming `MarkdownView`. Contrast test on the tokens. | The owner can chat with a bot in a browser from `dotnet run` of the AppHost. Component tests assert roles and names. Markdown sanitizer tests pass a hostile-input set. |
| 11 | **Bots and grants UI.** Bot editor with the full definition model and progressive disclosure. Tool grant controls. Built-in tools seeded per space. | Every bot field in the data model is editable in the UI and through the API. A bot with no grant for a tool does not receive that tool; asserted at the runtime boundary. |
| 12 | **MCP connections.** `IToolSource` over the MCP SDK. No-auth and header first, then OAuth with the callback bridge, token cache on the vault, catalog upsert with definition pinning, refresh and reconnect states. The tool gate with allow, ask, and deny, and the approval prompt. | Conformance suite against an in-process test MCP server for all three auth styles. A changed tool definition flips its grants to review. A per-user credential is unreadable in another user's transaction. |
| 13 | **Tool activity view.** Inline tool cards, the activity rail and timeline, the Activity screen, redaction on the way into events. | Each tool call shows arguments, live output, result, and outcome, live and from history. A seeded secret never appears in stored events or logs. |
| 14 | **Compose deployment.** Dockerfile, `compose.yaml` with the `init` service and volumes, start-up sequence, health endpoints, status page, `ComposeFileTests`. Record real build time, peak memory, and image size in the README. | On a clean machine: clone, `docker compose up`, finish setup in the browser, chat with a bot using an MCP tool. CI compose job is green inside the 2 GB budget. |
| 15 | **Daily-driver pass.** The owner uses it for a few days. Fix what hurts. Write the self-hosting README and the upgrade and backup notes. Tag `v0.1.0` through the release workflow. | The owner has stopped using what they used before. The release has a source archive, an SBOM, and attestations. |

## Sequencing notes

- Steps 1 to 5 produce no visible product. They are the part that cannot be added later without rework, and the part coding agents most need in place before they write features.
- Steps 7 and 8 are the technical heart. If step 7 shows the agent framework cannot end a turn cleanly on a pending tool call, stop and reassess before step 8; see the first risk in [12-risks.md](12-risks.md).
- Step 12 is the largest. If it runs long, ship no-auth and header first and OAuth as 12b; the owner can use Drover daily with header-auth servers meanwhile.
- From step 10 on, the owner can use the product locally every day, before the compose deployment exists.

## What Phase 0 leaves out, on purpose

Bots creating bots, delegation, sandboxes, native apps, push, attachments, memory, schedules running, search, cost screens, multiple users in the UI, skills, voice. The schema already has room for each, so none needs a redesign.

## Phase 1 and 2, in order

Not planned to the step, but sequenced by dependency and risk:

**Phase 1**

1. Hierarchy and delegation: management tools, child runs, subtree cancel, loop and budget checks, the Crew screen.
2. Usage and cost: records, roll-up, the per-bot and per-subtree views.
3. Sandboxes on E2B: adapter, broker, sharing policies, the terminal view.
4. Android UnifiedPush binding (start early, it is the riskiest client item), then Web Push end to end.
5. MAUI shell for Android and Windows.
6. Attachments, memory, schedules, web search.

**Phase 2**

1. Multiple users and shared spaces in the UI, external sign-in, passkeys, thread privacy.
2. Docker sandbox adapter and stdio MCP servers inside sandboxes.
3. Cross-tree delegation links with "borrowed authority asks".
4. Subscription sign-in for the providers that permit it.
5. Skill import. Voice.
6. Data export and account deletion.
7. Reaching bots from outside chat apps.
8. Apple: turn on the targets, build the relay, iOS push.
