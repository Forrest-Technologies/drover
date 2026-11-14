# 12. Risks, and the decisions I was least sure about

Ordered by how much damage each could do to the plan.

## Risks

| # | Risk | Why it is real | Mitigation | What would change the plan |
| --- | --- | --- | --- | --- |
| 1 | **Building on the agent framework fully, previews included, means absorbing its churn** | It reached 1.0 in April 2026 and ships weekly with breaking changes. Several features the design uses are marked experimental or are in preview packages. Their combined behavior (history saved per call, approval across a restart, checkpoint resume) is documented but not yet run by Drover. | All framework code is in one project. Conversation content is in Drover's own schema; framework sessions and checkpoints are disposable working state. Step 7 of Phase 0 proves the five integration points as tests before anything is built on them. A weekly canary job tests against the newest prerelease. | A proof in step 7 fails: Drover code takes over at that one seam (for example, saving approval state itself) and the rest stays on the framework. An experimental API is removed: same answer, scoped to that API. This is the cost of the owner's choice to use the framework fully, and it is accepted. |
| 2 | **The MCP SDK's OAuth flow fits a web server poorly** | Its client awaits the authorization code in process, with no way to persist a pending sign-in. | A bridge with a ten-minute in-memory wait and a database row; restart means "click connect again". | If users hit lost sign-ins in practice, or the bridge breaks on an SDK update, own the interactive leg and give the SDK finished tokens. Also revisit when the SDK ships a split API, which its roadmap points toward. |
| 3 | **Building the image on a small VPS is too slow or runs out of memory** | The .NET SDK image is large and a trimmed WebAssembly publish is memory-hungry. The owner ruled out hosting images. | A 2 GB memory budget enforced in CI on every pull request, with time and size reported. Build choices in [13-deployment-and-push.md](13-deployment-and-push.md). | If the budget cannot be held, or a first build takes long enough that people give up, this is an adoption risk to report to the owner with numbers. Options short of hosting images: publish a prebuilt web client as a release asset the Dockerfile downloads and verifies, so the server build is the only compile; or document a "build elsewhere, `docker save`, copy" path. |
| 4 | **E2B client owned by Drover drifts from the service** | No official .NET SDK. Processes and folder listings use Connect RPC with the JSON codec: each streamed message is a frame of one flag byte, a four-byte big-endian length, and JSON, ending with an end-of-stream frame that may carry an error. The framing comes from the Connect specification, not from E2B's code. The messages follow proto3 JSON rules that are easy to get wrong: zero values are left out (a missing exit code is 0), int64 is a string, bytes are base64, enums are names, times carry nine fraction digits. | Small surface, one project. Every fixture in CI is a stream or answer captured from the live service (`Fixtures/e2b`), and the same conformance theory runs live on demand (`E2BLiveConformanceTests`) and before each release. | An official .NET SDK appears: adopt it inside the adapter. The RPC proves brittle: use the REST file API plus a tiny helper process inside the sandbox image that Drover controls. |
| 5 | **Android push is client code Drover owns** | No .NET binding exists for the UnifiedPush connector, so the protocol is implemented directly in C#. The zero-setup fallback (embedded distributor as an unbound Android archive) is inferred from documentation, not yet tried. | The protocol is small and documented; another .NET app does the same. Prototype the fallback first in Phase 1. Everything sits behind one interface. | The fallback does not work unbound: port its registration call to C# as well, or ship without it and ask users to install a distributor app. |
| 6 | **A tenancy hole outside the tables** | Row-level security covers rows. It does not cover files on disk, in-memory caches, the live event feed, sandboxes, or log lines. | File keys are prefixed with the space id and resolved only through a `files` row. No cross-request cache holds tenant data in Phase 0. Live event subscribers are checked against the run's space when they subscribe, and events are read from the table under a tenant scope. Sandboxes are isolated per [07-sandbox.md](07-sandbox.md). | Before the hosted service opens: an external review of exactly these non-table paths. Budget for it. |
| 7 | **Prompt injection through tool results** | Unsolved in general. A bot with broad grants reads hostile text and acts on it. | Grants enforced outside the model, least privilege by default, definition pinning, tainted-run escalation, no automatic remote image loading. | If tainted-run escalation produces too many prompts and users turn it off wholesale, replace the single flag with per-tool data-flow labels. That is a larger design and is deliberately not built first. |
| 8 | **Volume of coding-agent output outruns review** | Most code will be agent-written. | The rules that matter are mechanical: analyzers, the isolation suite, the conformance suites, the license check. `AGENTS.md` explains the rest with reasons. | If agents repeatedly find ways around a rule, the rule becomes an analyzer or a test, not a longer paragraph in `AGENTS.md`. |
| 9 | **Losing the key ring loses every secret** | Data Protection keys live on their own volume by design. | Status page warning until a backup is confirmed; the README's backup section covers both volumes. | Nothing changes the design; this is an operations risk to keep visible. |
| 10 | **EF Core package lag** | `Pgvector.EntityFrameworkCore` has no release targeting EF Core 10. .NET 11 shipped yesterday and package ecosystems are mid-transition. | Track the newest generally available .NET ([ADR 0013](../adr/0013-track-the-newest-generally-available-dotnet.md)). EF Core and Npgsql stay one major behind until the provider is generally available. Vector search can fall back to one raw SQL query behind `IMemoryStore`. | Move EF Core to 11 when the Npgsql provider and `Pgvector.EntityFrameworkCore` support it. |
| 11 | **Subscription sign-in terms change** | Two large providers prohibit it today; one permits it only for open source and locally hosted apps, with request rules that are still labeled preview; terms have moved several times this year. | Key-only is always available. Sign-in adapters are separate and can be switched off per provider without a release, through a server setting. | Any provider tightening its terms: that adapter is disabled by default in the next release. |

| 12 | **Venice on the hosted service** | Self-hosting with the user's own key is fine. For a hosted service the provider's terms require flowing their terms down to end users, and the general license wording speaks of personal use. | Venice ships for self-hosting from Phase 0 regardless. | Before enabling Venice on the hosted service: the owner reads the current terms and, if unclear, asks the provider. |
| 13 | **ChatGPT plan sign-in needs a browser on the same machine as the callback** | The provider fixes the redirect to a loopback address and documents no device-code flow. | The desktop app or a small command completes the sign-in and hands the credential to the user's server. | The provider adds a device-code flow: use it and drop the hand-off. |

## Decisions I was least sure about

### 1. Blazor WebAssembly for the web, not server-side interactivity

Chosen because the MAUI shell forces an API-backed path anyway, and one path is simpler than two. The cost is a multi-megabyte first load and slower cold start on a weak phone browser.

Would change my mind: the owner finds first load on mobile browsers unacceptable in daily use. Then add static server rendering for the sign-in and setup screens only, keeping interactive screens on WebAssembly. Going to server-side interactivity for everything would mean maintaining two data paths, which I would resist.

### 2. Server-Sent Events, not a socket connection

Chosen for built-in resume and plain HTTP. The cost is one-directional streams; client actions are ordinary requests.

Would change my mind: voice (Phase 2) needs low-latency audio in both directions. That would add a socket for the voice session only, not replace the event stream.

### 3. A gapless per-tree event sequence taken under a row lock

Chosen for correctness and a trivial resume protocol. The cost is that every event append in a delegation tree serializes on one row. With coalescing at about ten appends per second per run, a tree of several concurrent bots is fine.

Would change my mind: a load test in Phase 1 showing lock waits with wide trees (many children streaming at once). Then give each run its own sequence and have the tree stream merge them with a per-run cursor map. More protocol, less contention.

### 4. Delegation only down the tree in Phase 1

Chosen because it cannot escalate privilege and cannot loop. The cost is that "ask the research team" from a sibling team has to go up through a common parent, or wait for Phase 2 links.

Would change my mind: the owner's own crew, in daily use, keeps needing sideways handoffs. Then bring delegation links and "borrowed authority asks" forward.

### 5. Credentials belong to users or spaces, never bots

Chosen so "who is this acting as" always has an answer and the narrowing rule stays simple. The cost: a bot cannot have its own service account distinct from the space's.

Would change my mind: a real need for per-bot service identities, such as two bots that must act as two different accounts on the same server. The clean way to add it is a second connection to the same server with its own shared credential, granted to one bot, which already works today without a model change.

### 6. Own UI components instead of a library

Chosen for the aesthetic and for the two screens no library has. The cost is building and testing about twenty components, including accessible menus and dialogs.

Would change my mind: if the component set is not solid by the end of Phase 0 step 10, adopt headless primitives for the hard widgets and keep Drover's styling on top.

### 7. No EF Core query filters as a second tenancy layer

Chosen to have one mechanism and no false comfort. The cost is that a missing tenant scope shows up as an exception or empty result at run time instead of being filtered quietly.

Would change my mind: I do not expect to. Quiet filtering is how tenancy bugs hide.

### 8. Space admins cannot read a member's private thread

Per-user privacy inside a shared space is enforced by row-level security, as the owner asked. A consequence: an admin of a shared space can see that a run happened, what it cost, and can cancel it, but cannot open a private thread's content. There is no override short of the member sharing the thread.

Would change my mind: a compliance need for admin review in shared spaces. The clean way to add it is an explicit, audited "review access" grant recorded in the database and checked by the policy, never a general bypass.

### 9. Approval moved into Phase 0

See [11-phase-0-plan.md](11-phase-0-plan.md). With the framework's approval support it is a small part of steps 7 and 13. I think shipping an "ask" grant that cannot ask is worse than the delay, but it is the owner's call.

## Things I checked and found no reason to change

- **Markdig.** Handles partial input acceptably for streaming, runs in WebAssembly and the MAUI web view unchanged, and is safe with raw HTML disabled plus a sanitizer pass.
- **PostgreSQL only.** Row-level security, job claiming with `SKIP LOCKED`, `LISTEN/NOTIFY`, partitioning, and vectors all come from the one database.
- **One server process.** Workers, scheduler, API, and static files in one container. The run engine's lease design already allows a second instance later without changes.
- **`.slnx`.** Works across the SDK, the build, formatting, CodeQL, and Dependabot.
- **Apache-2.0.** Every dependency chosen is compatible.

## Open items for the owner

1. Answered 2026-11-11: "BYOS" is bring your own subscription, including ChatGPT plan sign-in for self-hosted servers.
2. Approval in Phase 0: assumed yes; say if not.
3. Answered 2026-11-11: build on the agent framework fully, previews included. See [14-agent-framework.md](14-agent-framework.md).
4. Confirm that space admins should not be able to read members' private threads (decision 8 above).
