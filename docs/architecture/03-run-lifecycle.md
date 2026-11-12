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
| `waiting` | The run ended with a request that has no answer yet: an approval, or a workflow's question. No worker holds it. |
| `completed`, `failed`, `cancelled` | Terminal |

`status_reason` explains the current status in a short code: `awaiting_approval`, `awaiting_input`, `budget_exceeded`, `loop_detected`, `step_limit`, `cancelled_by_user`, `cancelled_by_parent`, `lease_lost`.

## From message to run

One transaction, opened by the API endpoint in the caller's tenant scope:

1. Insert the user message.
2. Insert a run: `status = queued`, `principal_user_id` = caller, `root_run_id` = its own id.
3. Append the `run.queued` event.
4. Commit, then signal the workers.

The endpoint returns the run id immediately. The client opens the event stream.

A thread runs one root run at a time. Posting takes a lock on the thread row first, so two posts to one thread take turns, and "is a run active" has one answer.

### A message sent while the bot is working

A message is never refused for arriving while a run is active (queued, running, or waiting). It is stored at once as **pending**: `position` is null, so it is not yet part of the conversation the model reads. The endpoint returns it with the run under way. The request says what should happen to it, in `whenBusy`:

| `whenBusy` | What happens |
| --- | --- |
| `steer` (the default) | The run under way takes the message at its next step. It is handed to the model after that step's own input, so after any tool results and never between a tool call and its result, and it gets its position in the same transaction that stores the step's reply. |
| `queue` | The message waits until the run has ended, then starts the next run. |

Rules that hold for both:

- **A run takes instructions only from the person it acts for.** A run uses its principal's authority, so in a shared thread another member's message never steers it: it is stored as `queue` whatever was asked, and is answered by a run that acts for its own author.
- **A pending message always gets a run.** When a root run ends for any reason (completed, failed, cancelled), the transaction that ends it queues the next run for the oldest pending message, as that message's author. A steering message that arrived after the run's last step is answered this way. The ending run takes the thread lock, the same lock a post takes, so a message cannot slip between "the run ended" and "nothing was pending".
- **A run that starts takes all of its person's pending messages**, in the order they were sent, and answers them together. Other members' messages wait for the run after it.
- `message.delivered` in the run's log says a pending message has its position, so a client can move it from "waiting" into the conversation.
- A steering message sent while the run waits for an approval stays pending until the approval is answered: the run has no step to take it at before then.

Stored order is the order things happened in. A run stopped in the middle of a tool call leaves that call without a result, and the result is stored later, after whatever was said in between. The history given to a model is therefore always put in call order first: every tool result directly after the message that made its call.

## Workers

`RunWorker` is a hosted service inside `Drover.Server`. It runs the engine's `RunDispatcher`, which executes a fixed number of runs at once (default 8, `Runs:Workers`). One loop claims for all of them, asking for as many runs as it has free workers: every call to `sys.claim_runs` writes an audit row, so an idle server asks once per poll, not once per worker. For each run:

1. **Claim.** Call `sys.claim_runs(worker_id, limit)`. It selects claimable runs with `FOR UPDATE SKIP LOCKED`, sets `lease_owner` and `lease_expires_at`, and returns only `(run_id, space_id, principal_user_id)`. This is one of the few cross-tenant functions; see [04-tenancy-and-permissions.md](04-tenancy-and-permissions.md).
2. **Enter scope.** Open a tenant scope for that space and principal. From here on the worker is subject to row-level security like any request.
3. **Execute** until the run waits or ends, renewing the lease every 15 seconds. While it executes, the worker reads the run row once a second: that is where it sees a cancel request and where it learns that another worker has taken the run, in which case it stops and writes nothing more.

Wake-up is an in-process signal, with a PostgreSQL `NOTIFY` on the channel `drover_runs`, sent in the transaction that queues the run, so a second server instance also wakes. The notification carries no payload. Each server holds one connection that listens for it. A poll every 5 seconds (`Runs:PollSeconds`) is the fallback, so a lost signal costs seconds, never correctness.

Every write an execution makes is fenced by its claim: the worker name and the attempt number the claim gave the run. A worker name is per process, so it cannot tell two claims by one server apart; the attempt can, because taking over a running run always raises it. Renewing the lease, appending events (and the usage or status change that travels with them), and the check before a model reply is stored all name the fence, and a write whose fence no longer matches changes nothing and stops that execution. So a server that stalls past its lease and then claims its own run again ends up with one execution that can write. A worker that cannot read its lease for 45 seconds stops as well.

A run that cannot even be loaded (its model key unreadable, say) does not loop: an unreadable key fails the run at once with `model_unavailable`, and any other load failure still honours a cancel request and fails the run when it is out of attempts.

A server that shuts down cleanly releases the leases of the runs it was executing, so the next server takes them over at once. That still counts as an attempt.

A claimable run is a root run that is `queued`, or `running` with an expired lease, or `waiting` with every pending request answered, or `waiting` with a cancel request (so a worker can end it; see Cancel a subtree).

## Executing a run

The model and tool loop is the agent framework's. Drover supplies storage and governance at the framework's extension points. The full map is in [14-agent-framework.md](14-agent-framework.md).

1. **Build.** In a short transaction, as the run's principal in the run's space, load the bot, its effective grants, its model connection and key, and what the event log says about the run so far. Build the framework agent from that data: instructions, model client, the granted tools only, context providers for memory and skills, a compaction strategy. Close the transaction. No transaction is ever held across a model or tool call.
2. **Load state.** The thread's saved session is read through `PostgresAgentSessionStore`. History comes from `messages` through `PostgresChatHistoryProvider`.
3. **Run.** The agent runs with streaming on. Updates are translated to Drover events as they arrive.
4. **Persist as it goes.** History is saved after every model call (the framework's per-service-call persistence). The framework saves a tool result together with the model call that follows it, so the tool gate writes the result to the `tool_calls` row as soon as the tool returns, and reading the history repairs a result the history is missing from that row (see [Server restart mid-run](#server-restart-mid-run)). Events, tool call rows, and usage are written in short transactions.
5. **Gate each tool call.** Function middleware hands every call to the tool gate:

   | Gate decision | When | Effect |
   | --- | --- | --- |
   | Run it | Effect is `allow` | A `tool_calls` row is saved as `running`, the tool runs, the redacted result is saved and returned to the model |
   | Ask | Effect is `ask`, or the run is tainted and the tool is sensitive | The tool is wrapped as approval-required, so the framework ends the run with an approval request. The row is saved as `pending_approval`. The wrapping happens when the agent is built: the framework can only stop for approval before it invokes a tool. A gate decision of `ask` for a tool that was not wrapped (a run that became tainted mid-step) is returned to the model as a refusal that says approval is needed; see open points in [14-agent-framework.md](14-agent-framework.md#results-of-the-step-7-spike). |
   | Refuse | No grant, bot paused, budget or loop check failed | A structured error result goes back to the model |

6. **Finish or wait.** If the run ended with a pending request, the session is saved and the run becomes `waiting`. Otherwise it is `completed`. The status change and the event that reports it commit in one transaction.

A **step** is one model call. The engine hands the runtime a model client of its own (`RunExecution.Model`) that wraps the provider's: every call through it is counted against the step limit (default 50 per run, `Runs:StepLimit`; the run fails with `step_limit`), announced as `step.started`, and refused outright if a tenant transaction is open in the calling flow. The step's message id is chosen before the call. The deltas carry it, and the runtime stores the reply under it, so `message.completed` names the row the deltas were building.

Read-only tools requested together run in parallel. Everything else runs in order: a call that is not read-only waits for the calls that are not read-only listed before it in the same model response.

## The runtime boundary

```csharp
namespace Drover.Domain.Agents;

public interface IAgentRuntime
{
    IAsyncEnumerable<AgentEvent> Execute(
        RunExecution execution,
        IRunGovernor governor,
        CancellationToken cancellationToken);
}


public interface IRunGovernor
{
    Task<ToolGateDecision> Decide(ToolCallRequest toolCall, CancellationToken cancellationToken);

    Task<ToolOutcome> InvokeTool(ToolCallRequest toolCall, CancellationToken cancellationToken);

    Task<ChildRun> BeginChildRun(DelegationRequest request, CancellationToken cancellationToken);

    Task CompleteChildRun(ChildRun child, ChildRunOutcome outcome, CancellationToken cancellationToken);

    Task RecordUsage(ModelUsage usage, CancellationToken cancellationToken);

    JsonElement Redact(JsonElement value);
}


public sealed record RunExecution
{
    public required Guid RunId { get; init; }

    public required Guid SpaceId { get; init; }

    public required Guid ThreadId { get; init; }

    public required Guid PrincipalUserId { get; init; }

    public required BotDefinition Bot { get; init; }

    public required IReadOnlyList<ToolDefinition> Tools { get; init; }

    public IModelChatClient? Model { get; init; }

    public IReadOnlyList<PendingResponse> Responses { get; init; } = [];
}
```

Every type in those signatures is Drover's. `Responses` carries answers to whatever the run was waiting on: an approval decision, or a reply to a workflow request. The runtime reads and writes the run's data as the principal in the run's space, so it needs the space, thread, and principal. `InvokeTool` is how a granted tool runs: the runtime never calls a tool itself, it hands the call, already approved by the gate, to the engine, which owns the tool sources. `Redact` is what the runtime passes tool arguments through before it stores them. `Model` is the model client the engine built for the run's bot; the engine owns it and counts every call made through it. The runtime writes the `tool_calls` row and the thread's messages; the engine writes run status, events, and usage.

`Drover.Agents` implements `IAgentRuntime` on the framework. The engine implements `IRunGovernor`. The interface exists so the run engine can be tested with a scripted runtime and no model; it is not a plan to replace the framework.

A conformance suite in `Drover.Adapters.Conformance` runs the runtime against a scripted fake model and asserts the event sequence for: plain reply, allowed tool, asked tool with a restart before the answer, refused tool, parallel read-only tools, cancellation mid-stream, kill mid-step and recover, delegation, and a team run resumed from a checkpoint. This suite is the gate for every framework update.

## Events

Each event is `(root_run_id, seq, run_id, type, data)`. `seq` is a gapless counter per root run.

### Why per root run

A delegation tree has several runs writing at once. A single sequence per tree lets one stream show the whole chain in order, and lets a client resume the whole tree with one number.

### How the sequence stays gapless

Appending events runs `UPDATE runs SET last_event_seq = last_event_seq + n WHERE id = root_run_id RETURNING last_event_seq` in the same transaction as the inserts. The row lock makes sequence order equal commit order. A reader can therefore never see event 12 before event 11 is visible, which a plain identity column cannot promise.

### Keeping the write rate sane

Model output arrives token by token. The engine coalesces deltas and flushes at most every 100 ms or 512 characters, whichever comes first, and always before any other event, so the log keeps the order things happened in. Persisted events are the coalesced chunks, and live subscribers receive exactly what was persisted, after commit. Live and replay are the same bytes: an event's data is published as the database returned it, because `jsonb` orders keys its own way.

Live delivery inside a server is a convenience, not the record. A subscriber that falls behind loses the oldest events from its queue and reads them from the table, which the gapless number makes safe.

### Event types

| Type | Data |
| --- | --- |
| `run.queued`, `run.started`, `run.waiting`, `run.completed`, `run.failed`, `run.cancelled` | Status, reason, error, usage totals |
| `step.started`, `step.discarded` | Step number and the step's message id. `step.discarded` tells clients to drop the partial output of a step whose model call was cut off: its message was never stored. |
| `message.delta` | Message id, part index, text chunk, part kind (`text` or `reasoning`) |
| `message.completed` | The final message as stored. Written for each model reply, once the runtime has stored it: before the reply's first tool is decided, before the next step, or when the run ends. |
| `message.delivered` | A message that was pending, as stored now that it has its position. Written when a run starts with pending messages, and before the `message.completed` of a step a message steered. |
| `tool.requested` | Tool call id, tool name, display title, redacted arguments, effect |
| `tool.approval_requested` | Tool call id, and the request id and model call id an answer must name |
| `tool.approved`, `tool.denied` | Tool call id, who decided. Written by the decision endpoint, while the run waits. |
| `tool.started`, `tool.output`, `tool.completed` | Tool call id, incremental output for long commands, outcome (`completed`, `failed`, `refused`, `interrupted`, `cancelled`), redacted result preview, duration |
| `delegation.started`, `delegation.completed` | Tool call id, child run id, child bot id, result summary |
| `usage.recorded` | Tokens and cost for one model call |

Tool activity is not a side channel. The activity view is a projection of these events plus the `tool_calls` rows.

The log closes what it opens. Every `step.started` is followed by that step's `message.completed` or by `step.discarded`, and every `tool.requested` by a `tool.completed` or a `tool.approval_requested`, whether the run finished, failed, was cancelled, or was taken over after a crash. A client that replays the log therefore shows what a reload of the thread would show. Payload records and type names are in `Drover.Contracts.Runs`; both sides read and write them through `RunEventTypes.Read` and `RunEventTypes.Write`. A contract carries free-form JSON (an event's data, a tool's arguments and result) as JSON text, never as a JSON type: the check that no contract can carry a secret reads every property a contract reaches, and a JSON type reaches everything.

## Streaming and resume

`GET /api/runs/{rootRunId}/events?after={seq}` returns Server-Sent Events. Each frame's `id` is the `seq`.

1. The server replays stored events with `seq > after` in pages, each page in its own short transaction.
2. When it reaches the end, it switches to the live feed. Because events are published after commit and are gapless, the handover check is a simple "next seq is last + 1"; on a gap it reads the missing rows from the table.
3. When the root run is terminal and everything is sent, the server closes the stream.

On reconnect the client sends `Last-Event-ID`, or `?after=`. The phone locking, the tab closing, or the server restarting all look the same to the client: reconnect with the last number it saw.

A second, lightweight stream, `GET /api/stream`, carries non-durable signals for the current user: "thread changed", "approval waiting", "run finished". It has no replay. On reconnect the client refetches lists. This keeps durable replay for the one thing that needs it.

Why Server-Sent Events and not a socket library: resume from a cursor is the built-in behavior of the protocol, it is plain HTTP for every proxy and for the MAUI `HttpClient`, and ASP.NET Core and `System.Net.ServerSentEvents` support both ends on .NET 10 and later with no extra package.

## Approval

1. The gate saves the tool call as `pending_approval`. The framework ends the run with an approval request, and the pending state is part of the saved session. The run becomes `waiting` with `awaiting_approval`, and the log holds `tool.approval_requested`.
2. The principal gets a signal on the space's stream and, if they are away, a push (push arrives with notifications).
3. They approve or deny through `POST /api/spaces/{spaceId}/tool-calls/{id}/decision`, with `approved` and an optional `reason`. The route carries the space, like every tenant route. The caller must be able to see the call, and of those who can, must be the run's principal or a space admin. So an admin answers a call in a shared thread, and cannot answer one in a private thread that is not theirs: nobody approves a call whose arguments they cannot read. A member who is not the principal cannot approve what a bot does with another member's authority. A call is answered once; a second answer gets 409.
4. The answer is stored on the `tool_calls` row (`approved`, or `denied` with the reason as the call's result, plus who decided and when) and appended to the log as `tool.approved` or `tool.denied`, in one transaction.
5. The run becomes claimable once every call it waits on is answered. The worker that takes it reads which requests the run last went to wait on from the log, reads their answers from the rows, and runs the agent again with them. Approved: the framework invokes the tool, through the gate. Denied: the model is told the person declined, with their reason.

The server can restart any number of times between steps 1 and 5. The session that holds the requests is saved again only when the run next stops, so an execution that takes over after a crash hands the same answers over again; the `tool_calls` row then decides what happens, and a call that had started is reported as interrupted, never run a second time.

A run that ends while a call still waits (it was cancelled) denies that call, with the reason that the run ended.

Not built yet: an approval that nobody answers is meant to expire (default 24 hours) as denied. Until then it waits until it is answered or the run is cancelled.

"Always allow" in the prompt changes the bot's grant from `ask` to `allow` when the user is permitted to do that, so the decision is visible in the bot editor and obeys the hierarchy rule. "Allow for this conversation" uses the framework's own approval rules, which are stored in the session.

## Delegation

Delegation is the built-in tool `delegate`. The child bot is exposed to its parent as a function (the framework's agent-as-tool), wrapped in `GovernedAgent`. When the parent's model calls it, the wrapper asks the governor to begin a child run. In one transaction the governor:

1. Checks the caller's grant and that the target is allowed ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)).
2. **Loop check.** Walks the caller's run ancestors. If the target bot already appears in that chain, refuses with `loop_detected`.
3. **Budget check.** Refuses if the tree would exceed the space's maximum delegation depth (default 5), maximum runs per tree (default 50), or cost budget per tree.
4. Creates a thread for the child (origin `delegation`, same audience as the root thread), a user-role message holding the task, and a child run with `parent_run_id`, `root_run_id`, `parent_tool_call_id`, `depth + 1`, and the same `principal_user_id`.
5. Saves the parent's tool call with `child_run_id`.

The child then runs as its own agent with **its own** grants, history, session, and events, inside the parent's tool call. When it finishes, its answer (or its failure) becomes the tool result and the child run is completed in one transaction.

`BeginChildRun` is idempotent on the parent's tool call id. If the server restarts while a child is working, the parent's step is re-entered, the same delegation is requested again, and the governor returns the existing child run, which continues from its own saved history instead of starting over.

If a child needs an approval, the whole tree pauses on it: the request surfaces, sessions are saved, and the root run becomes `waiting`.

A lease is held on the root run and covers its tree. Child runs are rows with their own status, usage, and events, executed by the worker that holds the root.

### Teams

Beyond one-to-one delegation, a parent bot can lead its children with one of the framework's orchestration patterns: sequential, concurrent, handoff, group chat, or a planner-led pattern. The pattern is data on the parent bot. A team run is a framework workflow:

- Each member is wrapped in `GovernedAgent`, so each still gets its own child run, grants, and events.
- Checkpoints are saved to PostgreSQL at each workflow step. After a restart the workflow resumes from the latest checkpoint.
- A workflow's request for human input surfaces as the same approval prompt and is re-raised after a resume.

Because every run in the tree shares `root_run_id`:

- The activity view shows the whole chain from one query and one stream.
- **Cost rolls up.** Each `usage_records` row carries `root_run_id`. When a run finishes, its usage is added to `subtree_usage` on each ancestor run. Per-bot totals are sums over `usage_records`; "this bot and everything it started" is a sum over its runs' `subtree_usage`.
- **Cancel a subtree.** `POST /api/spaces/{spaceId}/runs/{id}/cancel` sets `cancel_requested_at` on that run and every descendant run in one statement. The route carries the space, like every tenant route: a run cannot be found without knowing its tenant. The run's own principal may cancel, and so may a space admin. The worker executing the run is told at once when it is on the same server, and otherwise sees the flag on its next look at the run row (once a second); it cancels the model call, tool, or workflow, and writes `run.cancelled`. Queued and waiting runs are cancelled directly by the endpoint when the caller can see the run's thread. An admin who cannot see a private thread cannot write its events, so a worker ends the run for them, acting as the run's own principal: a cancel request makes a waiting run claimable at once, without waiting for an answer to its approval, and a queued run is claimable anyway. The parent of a cancelled child receives a "cancelled" tool result and continues, unless it was cancelled too.

## Server restart mid-run

Nothing in memory is needed to continue.

1. The lease of each in-flight run expires (45 seconds without renewal).
2. Another worker, or the same server after restart, claims it and increments `attempt`.
3. Recovery looks at the last step:

   | State found | Action |
   | --- | --- |
   | A model call was streaming | Append `step.discarded` and run the agent again. History already holds every model call that finished, and every tool result is in its `tool_calls` row, so only the interrupted call repeats. Where the provider supports background responses, the call is resumed from its continuation token instead. |
   | Tool call finished, history has no result for it | The result is taken from the `tool_calls` row and added to the history. The tool does not run again. |
   | Tool call `running`, tool is read-only by its definition | Run it again. The runtime does this when it reads the history, before the next model call and with no transaction open: it asks the governor to decide the call again, invokes the tool through the governor, writes the result to the row, and adds it to the history. If the bot may no longer make the call, it is settled as interrupted instead. Only Drover's own classification counts (`Sensitivity` is `Read`); an idempotency hint from a tool server is untrusted text and is not used. |
   | Tool call `running`, tool may have side effects | Mark it `interrupted` and give the model the result "interrupted, outcome unknown". The model decides what to do; if the grant is `ask`, the retry asks the human again. |
   | Tool call `pending_approval` | Nothing; the run stays `waiting` and its session holds the request |
   | Tool call approved and `running` when the process died | Mark it `interrupted`. The stale session makes the framework reject the approval on the next run; the `tool_calls` row wins over that result, so the model is told "interrupted, outcome unknown", not "rejected". |
   | A delegation was in progress | The delegation is requested again and resolves to the existing child run, which continues |
   | A team workflow was in progress | Resume from its latest checkpoint |

4. After three attempts the run fails with `lease_lost`.

The engine also repairs the event log when it takes a run over, from what the rows say. A step whose model call was cut off gets `step.discarded`, or `message.completed` if its message did reach the thread. A tool call the log never saw finish gets `tool.completed` with its row's outcome, or `interrupted`; a read-only one that was running is left open, because it runs again.

Drover never silently repeats an action that might have already happened. That is the honest limit of at-least-once execution, and the activity view shows `interrupted` so the human can see it.

Connected clients see the stream drop, reconnect with their last `seq`, and continue.

## Schedules

A schedule row holds a cron expression and time zone. A scheduler loop calls `sys.claim_due_schedules`, which returns due rows and advances `next_run_at` in one statement. For each, it opens a tenant scope with the schedule's owner as principal and creates a thread, message, and run exactly as the API endpoint does. From there a scheduled run is an ordinary run. The schema ships in Phase 0; the loop is Phase 1.
