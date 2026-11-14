# 14. Agent framework adoption

Owner direction, recorded 2026-11-11: use Microsoft Agent Framework as fully as possible, adopt its preview packages, and stay on its latest release. This section is the map of what Drover uses, how, and where Drover still has to add something of its own.

Everything named here was verified in the framework's source at version 1.28.0 and its main branch on 2026-11-10. Status words: **stable** (released package), **experimental** (marked experimental inside a stable package), **preview**, **alpha**.

## Principle

**The framework runs the agent. Drover governs it.**

- The framework owns: the model and tool loop, sessions, history handling, compaction, context and memory injection, skills, multi-agent orchestration, checkpoints, human-in-the-loop requests, telemetry, evaluation.
- Drover owns: who may do what (grants, hierarchy, audience), where state is stored (PostgreSQL, under row-level security), the run as a job (claim, lease, cancel, cost), and the event log clients read.

Drover does this the way the framework intends: by implementing its storage abstractions on PostgreSQL and by placing middleware and agent wrappers at its extension points. Drover does not re-implement the loop.

## Where it lives

`Drover.Agents` replaces the earlier idea of a thin adapter project. It is a first-class project that references the framework, `Drover.Domain`, and `Drover.Data`.

| Rule | Reason |
| --- | --- |
| Framework types appear in `Drover.Agents` and in `Drover.Server`'s wiring. Nowhere else. | The framework ships breaking changes weekly. One project absorbs them. |
| `Drover.Contracts`, `Drover.Domain`, `Drover.Engine`, `Drover.Ui`, and API responses stay free of framework types. | Clients and the database schema must not change when the framework does. |
| Conversation content is stored in Drover's own `messages` schema. | It is the product's data and must outlive any framework version. |
| Framework session state and workflow checkpoints are stored as opaque JSON with the framework version beside them. | They are working state, not records. If a new framework version cannot read an old blob, Drover discards it and rebuilds from `messages`. An in-flight run restarts its current step. |

The engine still talks to the framework through one small interface, `IAgentRuntime`, so the run engine can be tested without a model. That interface is now a seam for testing, not a hedge against the framework.

## Adoption map

### Core, from Phase 0

| Framework feature | Status | How Drover uses it |
| --- | --- | --- |
| `ChatClientAgent`, `AIAgent`, streaming runs | stable | Every bot is built into an agent from its definition at run start. Streaming updates become Drover run events. |
| `ChatHistoryProvider` | stable | `PostgresChatHistoryProvider` reads and writes the thread's `messages` inside a tenant scope, translating to Drover's part schema. No built-in PostgreSQL provider exists, so Drover supplies one. |
| Per-service-call history persistence (`RequirePerServiceCallChatHistoryPersistence`) | stable | On. Each model call is saved as it completes. A tool result is saved with the model call that follows it, not when the tool returns, so the tool gate writes every result to its `tool_calls` row and `PostgresChatHistoryProvider` repairs a missing result from that row when it reads the history. Together they are the crash-recovery hook: after a restart, nothing that finished is repeated. |
| `AgentSession`, session serialization, `AgentSessionStore` with `AgentSessionStoreKey` partitions | stable; store is experimental | `PostgresAgentSessionStore` keeps the serialized session per thread and bot in an `agent_sessions` table. Space and audience are key partitions and also row-level security columns, so the database enforces what the partition only names. The column is `json`, not `jsonb`, because the session's polymorphic JSON must keep its `"$type"` property first. |
| Function-invocation middleware | stable | The tool gate. Every tool call passes Drover's grant check, taint rule, redaction, and `tool_calls` bookkeeping here, including calls to tools a context provider adds. With per-service-call persistence the middleware's context carries no conversation, so the order of a response's calls comes from the history provider, which sees every response before its tools run. |
| `ApprovalRequiredAIFunction`, `ToolApprovalRequestContent`, `ToolApprovalResponseContent` | stable in `Microsoft.Extensions.AI` 10.11 | Tools whose effective grant is `ask` are wrapped. The run ends with an approval request; the framework records the pending request in the session, and on resume binds the response to it by request id, so a response cannot approve a call that was never surfaced. A pending request left unanswered on the next run is rejected by the framework. The run resumes with the response after any number of restarts. |
| Run and chat-client middleware | stable | Usage and cost recording, cancellation, secret redaction on the way to the provider's log. `ReplyRules`, a delegating chat client, holds each model call's answer until the bot's `before_reply` rules have seen all of it. `TransientImages`, a delegating chat client below it, shows the model a tool's picture on the next call only (below). |
| `UseOpenTelemetry` | stable | On for agents and workflows. Traces are exported only if the operator sets an endpoint. Message content is never attached. |
| Compaction strategies (sliding window, summarization, tool-result, truncation, pipeline) | experimental | A bot's memory settings choose a strategy and thresholds. Long threads stay usable without Drover writing a summarizer. |
| Evaluation (`EvaluateAsync`, local evaluators, expected tool calls) | stable | Used in Drover's own tests from Phase 0. Offered to users as per-bot checks in Phase 2. |

### Teams and delegation, Phase 1

| Framework feature | Status | How Drover uses it |
| --- | --- | --- |
| Workflows with `ICheckpointStore`, `CheckpointManager`, resume in a new process | stable | `PostgresCheckpointStore` (a JSON checkpoint store on a `workflow_checkpoints` table). A team run is a workflow; after a restart it resumes from its latest checkpoint. |
| `RequestPort`, request and response events | stable | Human-in-the-loop inside a team run. Pending requests survive in the checkpoint and are re-raised on resume. They surface in Drover as the same approval prompt. |
| Agent as a tool (`AsAIFunction`) | stable | The `delegate` tool. A parent bot calls a child bot as a function. |
| Orchestration builders: sequential, concurrent, handoff, group chat, Magentic | stable | "Team patterns" a human, or a permitted parent bot, picks for a group of bots. Stored as data on the parent bot. |
| `DelegatingAIAgent` and agent builder middleware | stable | `GovernedAgent`: every bot handed to a builder or exposed as a tool is wrapped first. The wrapper is where Drover checks the hierarchy rules, creates the child run row, applies the child's own grants, records cost, and emits delegation events. The framework has no per-invocation host callback, so the wrapper is the supported way to do this. |
| Sub-workflows, workflow as agent | stable; recovery helper experimental | A child bot that itself leads a team appears to its parent as one agent. |
| Background responses and continuation tokens | experimental | For providers that support them, a long model call survives a server restart by resuming from its token instead of starting over. |
| Harness: todo list, agent modes (plan, act), file memory | stable | Bot capabilities, switched on per bot. Their tools are ordinary tools in the catalog with grants. |
| Harness: file access over `AgentFileStore`, background sub-agents, loop agent with completion evaluators | experimental | `AgentFileStore` is implemented over the bot's sandbox or `IFileStore`. The loop agent backs "work until done" routines with a step and cost budget. |
| `AIContextProvider`, `TextSearchProvider`, `ChatHistoryMemoryProvider` | stable | Long-term memory and document search are context providers backed by `IMemoryStore` on PostgreSQL. |
| MCP tasks (`ListAgentToolsWithTasksAsync`) | alpha | Long-running MCP tools with polling and cancel, shown in the activity view as a tool that is still working. |
| Shell tool with a policy and pluggable executor | preview | Not used. `sandbox.run` is a Drover built-in tool (`SandboxTools`), because a tool the framework adds has no catalog row, so it has no grant and the tool gate refuses it. The command goes through `SandboxBroker` to `ISandboxProvider`. The local executor is never registered. |
| Micro virtual machine code execution | preview | Candidate for a second sandbox adapter that needs no account and no Docker, for "run this snippet" use. Evaluated in Phase 1; it does not replace a full environment. |

### Skills and reach, Phase 2

| Framework feature | Status | How Drover uses it |
| --- | --- | --- |
| Agent Skills (`AgentSkillsProvider`, custom `AgentSkillsSource`, script runner delegate) | stable | Skill import. A Drover source serves skills attached to the bot from the database. The script runner executes only inside the bot's sandbox. Loading, reading resources, and running scripts each require approval by default, which Drover keeps. |
| A2A client (remote agent as an `AIAgent`) | preview | An external agent can be added to a team as a member, wrapped in `GovernedAgent` like any bot. |
| A2A server | preview | A bot can be published as an A2A agent. Message-only since 1.28. |
| AG-UI endpoint | preview | A bot can be reached by any AG-UI client. Optional surface, off by default. |
| OpenAI-compatible chat completions endpoint | alpha | "Use your bot from any compatible client." Stateless facade only; its conversation storage is in-memory and not pluggable, so Drover does not use that part. |
| Declarative agents (YAML) | release candidate | Export and import of a bot definition. Drover's schema stays the source of truth. |
| Declarative workflows | stable | Evaluated for user-defined routines. Its agent actions are tied to one cloud service today, so this waits. |
| MCP-sourced skills | alpha | Skills offered by a connected MCP server, treated as untrusted like any import. |

### Development only

| Feature | Use |
| --- | --- |
| DevUI | Mounted by the AppHost in development to inspect agents and workflows. Never in the shipped server. |

### Not usable self-hosted

| Feature | Why |
| --- | --- |
| Durable task extension | Targets a cloud scheduler service. No self-hosted backend could be verified. Checkpoints, sessions, and history on PostgreSQL give Drover the same durability. |
| Compliance middleware tied to a cloud service | Needs that service. |
| Built-in store implementations for other databases | Reference only. Drover's are PostgreSQL. |

## What Drover must still add

The framework does not provide these, so Drover does:

| Gap | Drover's answer |
| --- | --- |
| Multi-tenant isolation | The framework has isolation keys and key partitions. They name a tenant; they do not stop a query. Row-level security does. |
| Per-bot, per-tool grants that narrow down a hierarchy | The tool gate middleware and `GovernedAgent`. |
| A durable event log clients can resume from | Drover's `run_events`. Framework streaming updates and workflow events are translated into it. |
| Recovery of a tool call that was in flight at a crash | Drover's `tool_calls` rows and the "interrupted, outcome unknown" rule ([03-run-lifecycle.md](03-run-lifecycle.md)). |
| Limits a person writes, enforced without asking the model | Bot rules. They run on the framework's own extension points, never beside them: the tool gate for a call and its result, a delegating chat client for a reply, the history provider for a message that steers ([03-run-lifecycle.md](03-run-lifecycle.md)). |
| Prompt-injection containment | The framework's proposed design for this has no .NET implementation. Drover's tainted-run rule and definition pinning stay ([06-mcp.md](06-mcp.md)). |
| Skill content is injected unsanitized | The framework's own source notes this. Drover keeps approval on, treats imports as untrusted, and runs scripts only in a sandbox. |
| Cost | Usage arrives in responses; pricing, roll-up, and budgets are Drover's. |

## Staying current

| Practice | Detail |
| --- | --- |
| Track the latest release, including previews | Dependabot proposes framework updates weekly as one grouped pull request. |
| The conformance suite is the gate | It runs the runtime through: plain reply, allowed tool, asked tool with restart before the answer, refused tool, parallel tools, cancel mid-stream, kill mid-step and recover, delegation, team run resumed from a checkpoint. An update merges when it is green. |
| Canary | A weekly scheduled job builds and tests against the newest prerelease versions and opens an issue when it fails, so a break is known before the release lands. |
| Read the release notes | Breaking changes are marked in each release. The update pull request links them. |
| Blob versioning | Sessions and checkpoints carry the framework version. Unreadable blobs are discarded, never migrated by hand. |
| Experimental APIs | Allowed. Each use is listed in `docs/architecture/05-dependencies.md` so an API that changes is easy to find. |

## What has to be proven first

These integration points exist and are documented, but their combined behavior has not been run by Drover yet. Phase 0 step 7 is a spike that proves each one with a test before the run engine is built on them:

1. History saved per service call lets a killed run continue without repeating a finished model call.
2. An approval request survives a process restart through the saved session and resumes correctly.
3. The function middleware sees every tool call, including calls made by a harness capability.
4. A workflow resumes in a new process from a PostgreSQL checkpoint, with a pending request re-raised. (Needed in Phase 1; proven early because the design leans on it.)
5. `GovernedAgent` wrapping holds inside each orchestration builder.

If one fails, the fallback for that point is Drover code at the same seam, and the finding is reported before work continues.

## Results of the step 7 spike

Run against Microsoft Agent Framework 1.28.0 and `Microsoft.Extensions.AI` 10.11.0, on PostgreSQL, with a scripted model. "Kill" is a new host with a new service provider; the old one is abandoned at the kill point without being cancelled, so nothing it would do afterwards happens.

| Proof | Result | Test |
| --- | --- | --- |
| 1. History saved per service call lets a killed run continue without repeating a finished model call | Pass, with one Drover addition at the history seam | `AgentRuntimeConformance.History_saved_per_service_call_lets_a_killed_run_continue_without_repeating_a_finished_model_call` |
| 2. An approval request survives a process restart through the saved session, and the tool then runs exactly once | Pass, with a Drover addition at the tool-call seam for a kill during the approved tool (point 8); a response with an unknown request id is rejected | `AgentRuntimeConformance.An_approval_request_survives_a_restart_and_the_tool_then_runs_exactly_once` |
| 3. The function middleware sees every tool call | Pass, including a call to a tool the todo harness capability added | `AgentFrameworkProofs.The_function_middleware_sees_every_tool_call_including_one_a_harness_capability_added` |
| 4. A workflow resumes in a new process from a PostgreSQL checkpoint with a pending request re-raised | Pass, on a `json` column, after the pending request's superstep checkpoint commits; the request id matches the first process's and the step before the checkpoint does not run again | `AgentFrameworkProofs.A_workflow_resumes_in_a_new_process_from_a_postgres_checkpoint_with_its_pending_request_raised_again` |
| 5. `GovernedAgent` is honored inside the sequential, concurrent, and handoff builders | Pass for all three, including a member's refused tool call stopped at the gate; a handoff itself is governed by the target's wrapper, not the gate (point 12) | `AgentFrameworkProofs.A_governed_agent_is_honored_inside_the_orchestration_builder` |

What the framework did that this document did not expect:

1. **A tool result is persisted with the next model call.** The per-service-call client saves a call's new input (the previous tool results) and its response after the call returns. A kill between a tool returning and the next call finishing leaves a tool call with no result in history. Drover's answer, at the history seam: the gate writes each result to `tool_calls` when the tool returns, and the history provider adds a missing result from that row, or "interrupted, outcome unknown" when the row shows the tool never finished (`A_tool_call_cut_off_by_a_kill_is_reported_interrupted_and_not_repeated`).
2. **`jsonb` breaks session and checkpoint deserialization.** The framework's polymorphic JSON requires `"$type"` first; `jsonb` reorders keys. `agent_sessions.state` is `json` (migration `AgentSessionStateKeepsKeyOrder`), and the Phase 1 `workflow_checkpoints.state` must be `json` too.
3. **The function middleware's context has no conversation** when history is persisted per service call (`FunctionInvocationContext.Messages` is empty). Ordering of calls that are not read-only uses the response the history provider stored instead.
4. **Approval can only be asked for before a tool is invoked.** The framework stops for approval only for a function wrapped as approval-required when the agent was built. A gate decision of `ask` at call time for an unwrapped tool cannot pause the run; the spike returns it to the model as a refusal that says approval is needed. This matters for the tainted-run rule, where a sensitive tool becomes `ask` mid-step. Proposed fallback, for the owner: when a run becomes tainted, end the step and rebuild the agent with sensitive tools wrapped, so the next model call that asks for one stops for approval.
5. **The tool approval types are stable** in `Microsoft.Extensions.AI` 10.11, no longer experimental. The only experimental APIs Drover uses are `AgentSessionStore` and `AgentSessionStoreKey` ([05-dependencies.md](05-dependencies.md#experimental-apis-in-use)).
6. **Pending approval requests live in the session's state bag**, and the framework binds a response to the recorded request by id, replacing whatever call the caller sent. Drover therefore passes only the request id, the call id, and the decision.
7. **The framework ships no fake chat client.** Drover's `ScriptedChatClient` lives in `Drover.Adapters.Tests`.
8. **A stale session can reject an approval that was acted on.** The framework clears its record of a pending approval only after the whole response that consumed it completes. A kill while the approved tool runs leaves the record in the saved session, and the next run gets a framework-made "rejected" result for a call that did run. Saving the session earlier does not help, because the record is still there until the response finishes. Drover's answer: the `tool_calls` row wins over any framework-made result, in the history read and in what is stored, and the gate claims a call through its row, so a call the row shows already started is never started again (`An_approved_tool_cut_off_by_a_kill_is_reported_interrupted_not_rejected`).
9. **Calls pair with results by `tool_calls` id, not by the model's call id.** Providers reuse call ids such as `c1` across turns; the row id is derived from the run and the call id, so one run's result cannot answer another run's call (`A_call_id_reused_by_a_later_run_is_repaired_from_that_runs_own_row`).
10. **A discarded session takes its pending approvals with it.** When a session is discarded (other framework version, or unreadable), the calls still waiting for approval are denied with a stated reason, which becomes their result in history (`A_pending_approval_in_a_discarded_session_expires_and_history_says_why`).
11. **Tool names must be unique in a run.** The framework rejects duplicate names itself; Drover rejects them first, with a clear message, including a capability tool named like a catalog tool. The gate identifies a catalog tool by the function object, never by name.
12. **A handoff does not pass the tool gate.** The handoff builder adds declaration-only functions and routes the call itself; nothing invokes a function, so no middleware sees it. A handoff is governed where it lands: the target is a `GovernedAgent`, which asks the governor for a child run. Team members are built the same way as a bot (`AsGovernedAgent`), so their own tool calls do pass the gate inside every builder. For the same reason a bot rule on tools cannot see a handoff itself; the member it lands on has its tool calls and its replies ruled, which the same proof asserts.
13. **A workflow request event precedes its checkpoint.** `RequestInfoEvent` is emitted during the superstep, before its checkpoint commits. Disposing the streaming run at that event can cancel checkpoint preparation and leave only the preceding checkpoint, so resume creates a new request. The checkpoint-resume proof waits for `SuperStepCompletedEvent` with a pending request and a non-null checkpoint before restarting, then verifies that the new host loads that checkpoint. Phase 1 must use this committed boundary before treating a workflow request as durable.

The runtime conformance suite (`Drover.Adapters.Conformance`, run by `Drover.Adapters.Tests`) covers plain reply, allowed tool, asked tool with a restart before the answer, denied approval, an unknown approval request id, an approved tool cut off by a kill, refused tool, parallel read-only tools, ordered write tools, cancel mid-stream, kill mid-step and recover, kill during a tool call, a read-only tool call cut off by a kill and run again, the same when the bot may no longer make the call, and a call id reused by a later run.

Added by the run engine (step 9), at the same seams:

1. **A model reply is stored under the id its model call gave it.** The engine's model client stamps every update of one call with one message id; the framework carries it to the reply it hands the history provider, which uses it as the row id. Only a reply keeps its id, never a request message. The engine's test `Deltas_and_the_completed_message_carry_the_id_the_message_is_stored_under` holds this through the real framework.
2. **A cut-off read-only call runs again in the history provider**, after its transaction has closed, through the governor, so the grant is checked again and the engine sees the call like any other.
3. **Tool arguments are redacted where they are stored**, in the `tool_calls` row and in the thread's message, through `IRunGovernor.Redact`. Results are redacted by the governor before it returns them.
4. **The chat client comes from the run.** `ChatClientResolver` takes the `RunExecution`, and the server gives it the model client the engine built (`RunExecution.Model`). Delegation and a team run resumed from a checkpoint are covered by proofs 4 and 5 against the framework directly; they join the suite when the engine has child runs (Phase 1).

Added for computer use ([07-sandbox.md](07-sandbox.md#computer-use)), at the chat-client seam:

1. **A tool's picture is shown to the next model call and stored nowhere.** A function result is JSON, and a chat completions tool message is text only, so a screenshot cannot travel as the result. `ToolOutcome.Images` carries it from the engine to the tool gate, which stores only the JSON result and keeps the latest picture in the `RunContext`. `TransientImages`, a `DelegatingChatClient` inside `ReplyRules` and so below the framework's per-service-call history persistence, takes it on the next model call and sends that call a new message list: the call's messages, then a user message with a line naming the tool call and the picture as `DataContent`. The persisting client keeps the list it was handed, not this one, so the picture never reaches `PostgresChatHistoryProvider`, the session, or `tool_calls`. Only the latest picture is kept: an older screenshot shows a screen that is gone. After a restart the model has the JSON result only. The image path has two branches, chosen in the engine before the framework sees anything: a bot whose own model reads the screen gets the picture this way; a bot with a screen reader set ([07-sandbox.md](07-sandbox.md#computer-use)) gets no picture at all, because the tool calls the reader through Drover's model seam (not through an agent, with no tools and no history) and returns its description as the JSON result, with `ToolOutcome.Images` empty, so `TransientImages` has nothing to add. Proof: `AgentFrameworkProofs.A_screen_read_by_another_model_reaches_a_text_only_model_as_words_and_no_image_content_is_ever_sent`. Chat-client middleware is used rather than a context provider because it sees every model call inside the tool loop, the very next of which must carry the picture, and it sits below the history persistence by construction. Proof: `AgentFrameworkProofs.A_tools_picture_reaches_the_next_model_call_only_and_is_never_stored`, which also reads the thread back in a new process and finds the result but no picture.
