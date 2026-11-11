# 3. Run lifecycle

A run is one bot working on one request. It is a database row that a worker executes in steps. Clients never drive a run; they watch its event log.

## States

```
                 +-------------------- cancel --------------------+
                 v                                                |
 queued --> running --> waiting --> running --> ... --> completed |
              |  ^         |                              failed  |
              |  +---------+ (pending tool call resolved)         |
              +--> failed / cancelled <---------------------------+
```

| Status | Meaning |
| --- | --- |
| `queued` | Waiting for a worker |
| `running` | A worker holds the lease and is executing a step |
| `waiting` | The turn ended with tool calls that have no result yet: an approval, a delegation, or both. No worker holds it. |
| `completed`, `failed`, `cancelled` | Terminal |

`status_reason` explains the current status in a short code: `awaiting_approval`, `awaiting_delegation`, `budget_exceeded`, `loop_detected`, `step_limit`, `cancelled_by_user`, `cancelled_by_parent`, `lease_lost`.

## From message to run

One transaction, opened by the API endpoint in the caller's tenant scope:

1. Insert the user message.
2. Insert a run: `status = queued`, `principal_user_id` = caller, `root_run_id` = its own id.
3. Append the `run.queued` event.
4. Commit, then signal the workers.

The endpoint returns the run id immediately. The client opens the event stream.

A thread runs one root run at a time. A message sent while a run is active is stored and queued behind it, or the user cancels and resends. This keeps message order unambiguous.

## Workers

`RunWorker` is a hosted service inside `Drover.Server`. A fixed number of worker loops (default 8, configurable) each do:

1. **Claim.** Call `sys.claim_runs(worker_id, limit)`. It selects claimable runs with `FOR UPDATE SKIP LOCKED`, sets `lease_owner` and `lease_expires_at`, and returns only `(run_id, space_id, principal_user_id)`. This is one of the few cross-tenant functions; see [04-tenancy-and-permissions.md](04-tenancy-and-permissions.md).
2. **Enter scope.** Open a tenant scope for that space and principal. From here on the worker is subject to row-level security like any request.
3. **Execute steps** until the run waits or ends, renewing the lease every 15 seconds.

Wake-up is a signal on an in-process channel, with a PostgreSQL `NOTIFY` so a second server instance also wakes. A poll every few seconds is the fallback, so a lost signal costs seconds, never correctness.

A claimable run is `queued`, or `running` with an expired lease, or `waiting` with no unresolved tool calls left.

## One step

A step is one model turn plus the tool calls it asks for.

1. **Assemble.** In a short transaction, load the bot, its effective grants, the thread's messages, and the tool definitions for granted tools only. Close the transaction. No transaction is ever held across a model or tool call.
2. **Run the turn.** Call `IAgentRuntime.RunTurn`. It streams `AgentEvent` values: text deltas, reasoning deltas, tool call requests, usage.
3. **Gate each tool call.** The runtime hands every tool call to `IToolGate`, which is the engine. The gate re-checks the grant, then returns one of:

   | Gate decision | When | Effect |
   | --- | --- | --- |
   | Result | Effect is `allow` and the tool finished | Result goes back to the model in the same turn |
   | Pending | Effect is `ask`, or the tool is a delegation | The tool call row is saved as `pending_approval` or `running` with a child run. The turn ends. |
   | Refused | No grant, bot paused, budget or loop check failed | A structured error result goes back to the model |

4. **Persist.** Assistant message parts, tool call rows, usage, and events are written in short transactions as they complete.
5. **Decide.** If any tool call is pending, set `waiting`. If the model finished with no tool calls, set `completed`. Otherwise loop to the next step, up to a step limit (default 50 per run).

Tool calls marked read-only by their definition run in parallel within a step. Everything else runs in order.

## The runtime boundary

```csharp
namespace Drover.Domain.Agents;

public interface IAgentRuntime
{
    IAsyncEnumerable<AgentEvent> RunTurn(
        AgentTurnRequest request,
        IToolGate toolGate,
        CancellationToken cancellationToken);
}


public interface IToolGate
{
    Task<ToolGateDecision> Decide(ToolCallRequest call, CancellationToken cancellationToken);
}


public sealed record AgentTurnRequest
{
    public required string Instructions { get; init; }

    public required IReadOnlyList<ConversationMessage> Messages { get; init; }

    public required IReadOnlyList<ToolDefinition> Tools { get; init; }

    public required ModelSelection Model { get; init; }
}
```

Every type in those signatures is Drover's. The request carries the full history each turn, so the runtime needs no memory of its own.

How the Agent Framework adapter fills the gaps (all behavior verified against version 1.28):

| Need | Framework behavior | What the adapter does |
| --- | --- | --- |
| Persistence | Sessions serialize to JSON; built-in history stores are in-memory or a cloud database | Ignores sessions. Builds a fresh agent per turn from Drover's messages. |
| Resume after restart | Workflow checkpoints, in-memory store only in C#; the durable extension needs a cloud service | Not used. Drover's step log is the checkpoint. |
| Intercept each tool call | Function-calling middleware on agents backed by the function-invoking chat client | Registers one middleware that forwards every call to `IToolGate` |
| Human approval | Approval-required functions end the run with an approval request | Tools whose effect is `ask` are wrapped that way, so the turn ends cleanly and the adapter emits a pending decision |
| Bot-to-bot handoff | Workflow orchestrations and agent-as-tool, in process | Not used across bots. Delegation is a Drover child run so it gets its own grants, budget, and cancel. |
| Streaming | `RunStreamingAsync` yields response updates | Mapped one-to-one to `AgentEvent` |

If the framework is replaced, one project changes. A conformance suite in `Drover.Adapters.Conformance` runs any `IAgentRuntime` against a scripted fake model and asserts the event sequence for: plain reply, allowed tool, asked tool, refused tool, parallel read-only tools, and cancellation mid-stream.

## Events

Each event is `(root_run_id, seq, run_id, type, data)`. `seq` is a gapless counter per root run.

### Why per root run

A delegation tree has several runs writing at once. A single sequence per tree lets one stream show the whole chain in order, and lets a client resume the whole tree with one number.

### How the sequence stays gapless

Appending events runs `UPDATE runs SET last_event_seq = last_event_seq + n WHERE id = root_run_id RETURNING last_event_seq` in the same transaction as the inserts. The row lock makes sequence order equal commit order. A reader can therefore never see event 12 before event 11 is visible, which a plain identity column cannot promise.

### Keeping the write rate sane

Model output arrives token by token. The engine coalesces deltas and flushes at most every 100 ms or 512 characters, whichever comes first. Persisted events are the coalesced chunks, and live subscribers receive exactly what was persisted, after commit. Live and replay are the same bytes.

### Event types

| Type | Data |
| --- | --- |
| `run.queued`, `run.started`, `run.waiting`, `run.completed`, `run.failed`, `run.cancelled` | Status, reason, error, usage totals |
| `step.started`, `step.discarded` | Step number. `step.discarded` tells clients to drop partial output from a step that was restarted. |
| `message.delta` | Message id, part index, text chunk, part kind (`text` or `reasoning`) |
| `message.completed` | The final message as stored |
| `tool.requested` | Tool call id, tool name, display title, redacted arguments, effect |
| `tool.approval_requested`, `tool.approved`, `tool.denied` | Tool call id, who decided |
| `tool.started`, `tool.output`, `tool.completed` | Tool call id, incremental output for long commands, outcome, redacted result preview, duration |
| `delegation.started`, `delegation.completed` | Tool call id, child run id, child bot id, result summary |
| `usage.recorded` | Tokens and cost for one model call |

Tool activity is not a side channel. The activity view is a projection of these events plus the `tool_calls` rows.

## Streaming and resume

`GET /api/runs/{rootRunId}/events?after={seq}` returns Server-Sent Events. Each frame's `id` is the `seq`.

1. The server replays stored events with `seq > after` in pages, each page in its own short transaction.
2. When it reaches the end, it switches to the live feed. Because events are published after commit and are gapless, the handover check is a simple "next seq is last + 1"; on a gap it reads the missing rows from the table.
3. When the root run is terminal and everything is sent, the server closes the stream.

On reconnect the client sends `Last-Event-ID`, or `?after=`. The phone locking, the tab closing, or the server restarting all look the same to the client: reconnect with the last number it saw.

A second, lightweight stream, `GET /api/stream`, carries non-durable signals for the current user: "thread changed", "approval waiting", "run finished". It has no replay. On reconnect the client refetches lists. This keeps durable replay for the one thing that needs it.

Why Server-Sent Events and not a socket library: resume from a cursor is the built-in behavior of the protocol, it is plain HTTP for every proxy and for the MAUI `HttpClient`, and ASP.NET Core and `System.Net.ServerSentEvents` support both ends on .NET 10 with no extra package.

## Approval

1. The gate saves the tool call as `pending_approval` and the turn ends. The run becomes `waiting`.
2. The principal gets a signal on `/api/stream` and, if they are away, a push.
3. They approve or deny through `POST /api/tool-calls/{id}/decision`. The endpoint checks that the caller is the run's principal or a space admin.
4. Approved: the call is executed by a worker, its result is stored. Denied: a "denied by user" result is stored.
5. With no unresolved tool calls left, the run is claimable again and the next step starts with the results in its history.

An approval that nobody answers expires (default 24 hours) as denied.

## Delegation

Delegation is the built-in tool `delegate`. Its gate path, in one transaction:

1. Check the caller's grant and that the target is allowed ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)).
2. **Loop check.** Walk the caller's run ancestors. If the target bot already appears in that chain, refuse with `loop_detected`.
3. **Budget check.** Refuse if the tree would exceed the space's maximum delegation depth (default 5), maximum runs per tree (default 50), or cost budget per tree.
4. Create a thread for the child (origin `delegation`), a user-role message holding the task, and a child run with `parent_run_id`, `root_run_id`, `parent_tool_call_id`, `depth + 1`, and the same `principal_user_id`.
5. Save the parent's tool call with `child_run_id`. The parent turn ends and the parent run waits.

When the child reaches a terminal status, the engine writes the child's final answer (or its failure) as the result of the parent's tool call, in the same transaction that completes the child. The parent becomes claimable.

Because every run in the tree shares `root_run_id`:

- The activity view shows the whole chain from one query and one stream.
- **Cost rolls up.** Each `usage_records` row carries `root_run_id`. When a run finishes, its usage is added to `subtree_usage` on each ancestor run. Per-bot totals are sums over `usage_records`; "this bot and everything it started" is a sum over its runs' `subtree_usage`.
- **Cancel a subtree.** `POST /api/runs/{id}/cancel` sets `cancel_requested_at` on that run and every descendant run in one statement. Workers check the flag between events and when renewing the lease, cancel the model call or tool, and write `run.cancelled`. Waiting runs are cancelled directly. The parent of a cancelled subtree root receives a "cancelled" tool result and continues, unless it was cancelled too.

## Server restart mid-run

Nothing in memory is needed to continue.

1. The lease of each in-flight run expires (45 seconds without renewal).
2. Another worker, or the same server after restart, claims it and increments `attempt`.
3. Recovery looks at the last step:

   | State found | Action |
   | --- | --- |
   | Model turn was streaming, no tool call started | Append `step.discarded`, rerun the step |
   | Tool call `running`, tool is read-only or idempotent by its definition | Run it again |
   | Tool call `running`, tool may have side effects | Mark it `interrupted` and give the model the result "interrupted, outcome unknown". The model decides what to do; if the grant is `ask`, the retry asks the human again. |
   | Tool call `pending_approval` or waiting on a child | Nothing; the run stays `waiting` |

4. After three attempts the run fails with `lease_lost`.

Drover never silently repeats an action that might have already happened. That is the honest limit of at-least-once execution, and the activity view shows `interrupted` so the human can see it.

Connected clients see the stream drop, reconnect with their last `seq`, and continue.

## Schedules

A schedule row holds a cron expression and time zone. A scheduler loop calls `sys.claim_due_schedules`, which returns due rows and advances `next_run_at` in one statement. For each, it opens a tenant scope with the schedule's owner as principal and creates a thread, message, and run exactly as the API endpoint does. From there a scheduled run is an ordinary run. The schema ships in Phase 0; the loop is Phase 1.
