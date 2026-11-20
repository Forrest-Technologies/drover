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

`status_reason` explains the current status in a short code: `awaiting_approval`, `awaiting_takeover` (a call asked a person to take over the bot's screen; see [07-sandbox.md](07-sandbox.md#a-person-takes-over)), `awaiting_input`, `budget_exceeded`, `loop_detected`, `step_limit`, `token_limit`, `cancelled_by_user`, `cancelled_by_parent`, `lease_lost`.

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
- **A message a rule withheld is the exception.** It is marked `withheld`, gets its position when the run ends so people still read it where it was sent, and starts no run.
- `message.delivered` in the run's log says a pending message has its position, so a client can move it from "waiting" into the conversation.
- A steering message sent while the run waits for an approval stays pending until the approval is answered: the run has no step to take it at before then.

Stored order is the order things happened in. A run stopped in the middle of a tool call leaves that call without a result, and the result is stored later, after whatever was said in between. The history given to a model is therefore always put in call order first: every tool result directly after the message that made its call.

### Versions of a conversation

A person may edit a message of theirs or ask for another reply, and switch between the versions that made. Nothing is deleted. Each message names the message before it in its version (`parent_id`); the messages that share a parent are versions of one place: a person's message and its edits, or the first messages of each reply to one message. One version of each place is shown. The rest are **shelved** (`shelved_at` is set): kept with their runs, events, tool calls, and usage as they were, left out of the conversation the API lists and the model reads, and brought back when their version is chosen again.

| Request | What happens |
| --- | --- |
| `POST .../threads/{threadId}/messages/{messageId}/regenerate` | `messageId` is a bot's reply. The last message of a person before it is answered again: everything after that message is shelved and a run is queued for it, as for a new message. The run answers with the conversation's current model. Returns the person's message and the run. |
| `POST .../threads/{threadId}/messages/{messageId}/edit` | `messageId` is a person's message. Its edit is stored in its place, after the same message, with the same position; the message and everything after it are shelved, and a run answers the edit. The edit replaces the words; files sent with the message go with it. Returns the edit and the run. |
| `POST .../threads/{threadId}/messages/{messageId}/show` | Shows that version of its place in place of the one shown, and with it the messages that followed it when it was left: from each message, the one shelved last, which is the one that was shown. 204. |

Rules that hold for all three:

- **Only while nothing works on the thread.** A run that is queued, running, or waiting answers 409: the version shown is what the next run reads, so it does not move under a run. The bot must be active to answer again (409 otherwise).
- **Only the message's author asks for a new version.** Asking again or editing starts a run that acts for the caller, from their own words; another member's message answers 403. Anyone who can read the conversation may switch versions, since nothing is lost. 404 to anyone who cannot read the thread, and for a message that is not shown or is not the kind asked for.
- **The model reads the version shown.** History comes only from the messages shown (`MessageRepository.ListMessages`), so a run answering again sees the conversation up to the message it answers and nothing of the version left. The framework's session for the thread is discarded when the version changes; it is working state of the version left, and the next run starts from the messages ([14-agent-framework.md](14-agent-framework.md)). A conversation summary that covers any shelved message is dropped and written again later.
- **Runs of an earlier version stay.** Their rows, logs, tool calls, and usage are untouched, so they stay in the run history, the activity view, and every usage figure. `MessageResponse.Versions` lists the ids of every version of a message's place, oldest first, when there is more than one.

A position is unique among the messages shown, not among all of them: an edit takes the position of the message it edits, and another reply the positions of the one before it. The data model allows a new version at any message; the clients offer it at the last message and the last reply.

## Project context on a run

A conversation in a project gives that run two more inputs. Both are read as the run's person, in a tenant transaction, against one pinned project loaded with the bot, before the run persists context.

The project's instructions are a labeled source beside the bot's own instructions. They are loaded with the bot and the project row. They are not copied into the bot's instructions. They do not add a tool, change the model, or change sandbox access. A conversation in no project is given the bot's instructions exactly as before. The next execution reads the project again, the same way it reads the bot. Moving the conversation between projects while an execution is active keeps the pinned execution project for both instructions and files until the next execution starts. The text is not stored on the run or in the conversation.

The project's files go through the direct grant path (`RunFiles.OpenProject`) without copying anything into a sandbox. In the history transaction, the history provider reads a bounded lightweight window from the pinned project's current files, counts omissions separately, and applies `ProjectFileGrant.SelectForOpen` within `ProjectFileLimits.MaxFiles` and `ProjectFileLimits.MaxStoredBytes`, newest first, before any file bytes are opened. After the transaction closes, `RunFiles.OpenProject` verifies each row still belongs to that project and opens only text, read documents, and images for vision models. One model call is also bounded by rendered characters, image count, encoded image bytes, decoded image bytes, dimensions, and pixel count. Images that exhaust the shared call allowance are reported in words while older files continue; scanned PDFs keep the pages that fit and state the shown and total page counts. Archives, unsupported kinds, and intrinsically oversized images are left closed and reported in words without touching a sandbox. Stable text and refusal results are retained for the run. Image bytes are retained only for the current selection and never beyond one call's image allowance, then released when the selection changes or empties. Their content is untrusted and taints the run before it is opened (so a file added mid-run escalates any unbuilt `allow` write tool before the next model call). A file removed from the project is not granted on the next read. Two bots in one project each keep their own tool grants and their own sandbox. A private conversation stays unreadable to another member who can read the project.

Proof: `ProjectRunContextTests` in `Drover.Engine.Tests`, and `ProjectApiTests.A_private_conversation_in_a_project_stays_unreadable` for the HTTP reads.

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

1. **Build.** In a short transaction, as the run's principal in the run's space, load the bot, its effective grants, its model connection and key, and what the event log says about the run so far. Build the framework agent from that data: instructions, model client, the granted tools only, and, when the run is offered a memory tool, the bot's notes about the run's principal, read in the same transaction and given to every model call as reference material ([14-agent-framework.md](14-agent-framework.md)). No framework compaction strategy is set; the one compaction Drover applies, a main thread's summary, is applied by the history provider ([Conversation summaries](#conversation-summaries)). Close the transaction. No transaction is ever held across a model or tool call.
2. **Load state.** The thread's saved session is read through `PostgresAgentSessionStore`. History comes from `messages` through `PostgresChatHistoryProvider`, beginning with the thread's summary instead of the messages it covers when the bot compacts by summarization ([Conversation summaries](#conversation-summaries)).
3. **Run.** The agent runs with streaming on. Updates are translated to Drover events as they arrive.
4. **Persist as it goes.** History is saved after every model call (the framework's per-service-call persistence). The framework saves a tool result together with the model call that follows it, so the tool gate writes the result to the `tool_calls` row as soon as the tool returns, and reading the history repairs a result the history is missing from that row (see [Server restart mid-run](#server-restart-mid-run)). Events, tool call rows, and usage are written in short transactions.
5. **Gate each tool call.** Function middleware hands every call to the tool gate:

   | Gate decision | When | Effect |
   | --- | --- | --- |
   | Run it | Effect is `allow` | A `tool_calls` row is saved as `running`, the tool runs, the redacted result is saved and returned to the model |
   | Ask | Effect is `ask`; or it is `allow` on a built-in tool that always asks (`crew.add`); or the run is tainted and the tool is sensitive | The tool is wrapped as approval-required, so the framework ends the run with an approval request. The row is saved as `pending_approval`. The wrapping happens when the agent is built: the framework can only stop for approval before it invokes a tool. A gate decision of `ask` for a tool that was not wrapped (a run that became tainted mid-step) is returned to the model as a refusal that says approval is needed; see open points in [14-agent-framework.md](14-agent-framework.md#results-of-the-step-7-spike). |
   | Refuse | No grant, bot paused, budget or loop check failed | A structured error result goes back to the model |

6. **Finish or wait.** If the run ended with a pending request, the session is saved and the run becomes `waiting`. Otherwise it is `completed`. The status change and the event that reports it commit in one transaction.

A **step** is one model call. The engine hands the runtime a model client of its own (`RunExecution.Model`) that wraps the provider's: every call through it is counted against the run's limits (below), announced as `step.started`, and refused outright if a tenant transaction is open in the calling flow. The step's message id is chosen before the call. The deltas carry it, and the runtime stores the reply under it, so `message.completed` names the row the deltas were building.

**Run limits.** Three limits stop a run that runs away before it spends a person's model credit. Each is a deployment ceiling ([16-deployment-policies.md](16-deployment-policies.md)) that the space may lower in its settings and the bot may lower again in its own `run_limits` (the editor's Limits section); no level raises the one above (`PolicyService.Runs`, then `RunCeilings.NarrowedBy`). They are read when a run is loaded and held by its `RunSession`.

| Limit | Deployment rule | Space | Bot | When it is reached |
| --- | --- | --- | --- | --- |
| Model calls | `MaxStepsPerRun` (50) | `max_steps_per_run` | `max_steps` | Checked before every model call, a tool's own included (the screen reader). The run fails with `step_limit`. |
| Tokens | `MaxTokensPerRun` (5,000,000; hosted 2,000,000) | `max_tokens_per_run` | `max_tokens` | Input and output tokens of every model call the run has recorded, earlier executions included, checked before every model call. The run fails with `token_limit`. The call that takes the run past the limit finishes and is recorded, so a run can end above it by one call. |
| Handoffs | `MaxRunsPerDelegationTree` less the root | (through the tree's limit) | `max_delegations` | Counted from the run's child runs when it calls `delegate`. The call is refused with `budget_exceeded`, detail `handoffs`; the run goes on with what it has. `0` keeps a bot from handing work over at all. |

A child run has limits of its own, from its own bot. The tree as a whole is bounded by the delegation budget in [Delegation](#delegation). A run stopped by `step_limit` or `token_limit` shows in its thread as `budget_reached`, and its error names the limit, the value, and where to change it.

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

    Task<ChildRunReport> RunChildRun(ChildRun child, CancellationToken cancellationToken);

    Task<ToolOutcome> CompleteChildRun(ChildRun child, ChildRunReport report, CancellationToken cancellationToken);

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

Every type in those signatures is Drover's. `Responses` carries answers to whatever the run was waiting on: an approval decision, or a reply to a workflow request. The runtime reads and writes the run's data as the principal in the run's space, so it needs the space, thread, and principal. `InvokeTool` is how a granted tool runs: the runtime never calls a tool itself, it hands the call, already approved by the gate, to the engine, which owns the tool sources. `Redact` is what the runtime passes tool arguments through before it stores them. `BeginChildRun`, `RunChildRun`, and `CompleteChildRun` are delegation's three steps: begin the child run (or find the one begun for the same call), execute it as a run of its own, and turn what it came to into the parent's result. `Model` is the model client the engine built for the run's bot; the engine owns it and counts every call made through it. The runtime writes the `tool_calls` row and the thread's messages; the engine writes run status, events, and usage.

`Drover.Agents` implements `IAgentRuntime` on the framework. The engine implements `IRunGovernor`. The interface exists so the run engine can be tested with a scripted runtime and no model; it is not a plan to replace the framework.

A conformance suite in `Drover.Adapters.Conformance` runs the runtime against a scripted fake model and asserts the event sequence for: plain reply, allowed tool, asked tool with a restart before the answer, refused tool, parallel read-only tools, cancellation mid-stream, kill mid-step and recover, delegation, and a team run resumed from a checkpoint. This suite is the gate for every framework update.

## Events

Each event is `(root_run_id, seq, run_id, type, data)`. `seq` is a gapless counter per root run.

### Why per root run

A delegation tree has several runs writing at once. A single sequence per tree lets one stream show the whole chain in order, and lets a client resume the whole tree with one number.

### How the sequence stays gapless

Appending events runs `UPDATE runs SET last_event_seq = last_event_seq + n WHERE id = root_run_id RETURNING last_event_seq` in the same transaction as the inserts. The row lock makes sequence order equal commit order. A reader can therefore never see event 12 before event 11 is visible, which a plain identity column cannot promise.

### Keeping the write rate sane

Model output arrives token by token, and a tool's output piece by piece. The engine coalesces both (deltas per message part, tool output per call and stream) and flushes at most every 100 ms or 512 characters, whichever comes first, and always before any other event, so the log keeps the order things happened in. Persisted events are the coalesced chunks, and live subscribers receive exactly what was persisted, after commit. Live and replay are the same bytes: an event's data is published as the database returned it, because `jsonb` orders keys its own way.

Live delivery inside a server is a convenience, not the record. A subscriber that falls behind loses the oldest events from its queue and reads them from the table, which the gapless number makes safe.

### Event types

| Type | Data |
| --- | --- |
| `run.queued`, `run.started`, `run.waiting`, `run.completed`, `run.failed`, `run.cancelled` | Status, reason, error, usage totals |
| `step.started`, `step.discarded` | Step number and the step's message id. `step.discarded` tells clients to drop the partial output of a step whose model call was cut off: its message was never stored. |
| `message.delta` | Message id, part index, text chunk, part kind (`text` or `reasoning`) |
| `message.completed` | The final message as stored. Written for each model reply, once the runtime has stored it: before the reply's first tool is decided, before the next step, or when the run ends. Written again for a reply a tool added to, as stored then (a file the bot shared); a client replaces the message it holds by id. |
| `message.delivered` | A message that was pending, as stored now that it has its position. Written when a run starts with pending messages, and before the `message.completed` of a step a message steered. |
| `tool.requested` | Tool call id, tool name, display title, redacted arguments, effect |
| `tool.approval_requested` | Tool call id, and the request id and model call id an answer must name. `details`, when the tool adds any: things the person should see beside the arguments, each a name and a redacted text (`crew.add` adds `model` and `beneath`, the new bot's model and the name of the bot it goes beneath) |
| `rule.fired` | A rule of the bot applied: rule id and name, checkpoint, effect, a sentence saying why, and the tool call id when there is one. Never the text that matched. |
| `tool.approved`, `tool.denied` | Tool call id, who decided. Written by the decision endpoint, or the hand-back endpoint for a request for a person at the screen, while the run waits. |
| `tool.started` | Tool call id |
| `tool.output` | Tool call id, stream (`stdout` or `stderr`), text. Output a built-in tool streams while it runs (a sandbox command's output), redacted, between the call's `tool.started` and `tool.completed`. One call streams at most `Runs:MaxToolOutputCharacters` (200,000 by default); past it the log says once that the rest is left out. The tool's result is separate and not affected. |
| `tool.completed` | Tool call id, outcome (`completed`, `failed`, `refused`, `interrupted`, `cancelled`), redacted result preview, duration |
| `delegation.started`, `delegation.completed` | Tool call id, child run id, child bot id; the child's thread id (`started`; events written before it was recorded have none); outcome and result summary (`completed`) |
| `usage.recorded` | Tokens and cost for one model call; the cost is null when the price is not known |

Tool activity is not a side channel. The activity rail is a projection of these events alone, read from the root run's one stream: each event goes to its own run's view, and `delegation.started` names the bot of each child run, so one stream lays out the whole tree. The whole result of a call the thread's own bot made comes from the thread's stored messages (the `tool_result` part); a call further down shows the start of its result that `tool.completed` keeps.

The log closes what it opens. Every `step.started` is followed by that step's `message.completed` or by `step.discarded`, and every `tool.requested` by a `tool.completed` or a `tool.approval_requested`, whether the run finished, failed, was cancelled, or was taken over after a crash. The one call left open is a delegation whose child waits for a person: it has `delegation.started`, and is closed when the tree resumes and the child ends. A client that replays the log therefore shows what a reload of the thread would show. Payload records and type names are in `Drover.Contracts.Runs`; both sides read and write them through `RunEventTypes.Read` and `RunEventTypes.Write`. A contract carries free-form JSON (an event's data, a tool's arguments and result) as JSON text, never as a JSON type: the check that no contract can carry a secret reads every property a contract reaches, and a JSON type reaches everything.

## Streaming and resume

`GET /api/spaces/{spaceId}/runs/{rootRunId}/events?after={seq}` returns Server-Sent Events. The route carries the space, like every tenant route. Each frame's `id` is the `seq`, its `event` is the type, and its `data` is the event as `RunEventResponse`.

1. The server checks, in a short transaction as the caller, that the run is a root run of the space and that the caller may see its thread. Row-level security answers both: another member asking for a private thread's stream gets 404, and so does anyone outside the space.
2. It subscribes to the live feed, then replays stored events with `seq > after` in pages of 200, each page in its own short transaction. No transaction lives as long as a stream.
3. When it reaches the end, it switches to the live feed. Because events are published after commit and are gapless, the handover check is a simple "next seq is last + 1"; on a gap it reads the missing rows from the table.
4. When nothing arrives for 15 seconds (`Runs:HeartbeatSeconds`) it sends a comment line, so proxies keep the stream open, and reads the table again. That read is also how a watcher on one server sees events another server wrote.
5. When the root run is terminal and everything is sent, the server closes the stream. A client that had already seen everything gets a stream that closes at once. The server also closes its streams when it shuts down.

On reconnect the client sends `Last-Event-ID`, or `?after=`; the header wins when both are present. The phone locking, the tab closing, or the server restarting all look the same to the client: reconnect with the last number it saw.

A second, lightweight stream, `GET /api/spaces/{spaceId}/stream`, carries non-durable signals for the current user in that space: `thread.changed`, `approval.waiting`, `run.finished`, each naming the thread and the run, and `thread.seen`, naming the thread the person just opened on one of their screens. Answering an approval sends `thread.changed` to the run's person. It has no replay and is delivered only by the server that made the change. On reconnect the client refetches lists; the client library says `reconnected` itself each time it has to connect again. This keeps durable replay for the one thing that needs it.

### What a thread has for a person

Each `ThreadSummary` carries `Attention`, computed for the caller from their own runs and their own read marker (`thread_reads`), never from another member's: `WaitingForYou` (their newest root run in the thread waits, and a call anywhere in its delegation tree still has no answer), `Failed` (their newest root run failed and they have not opened the thread since it ended), `HasNew` (a reply from a run of theirs past the last message they saw), and `LastFailure` (that newest root run while it is failed, seen or not: its id, a cause, and the error as the run's log holds it). Only root runs count for these: a child run's approval makes its root's thread read as waiting, the person's one place to answer, and a child that failed is its parent's to recover from, so it makes no thread read as failed and cannot be tried again by the person. The cause is one of `model_unreachable`, `model_refused`, `model_busy` (the provider is limiting requests), `model_out_of_credit` (the provider account is out of credit), `budget_reached` (a Drover limit), `stopped_by_rule`, `server_restarted`, or `unknown`, from the run's error code. The crew screen reads every thread on first paint, then reads one thread again for each signal that names it. Opening a conversation marks it seen (`PUT .../threads/{threadId}/seen`), which sends `thread.seen`, so the person's other screens clear it within a second.

`POST .../runs/{runId}/retry` starts a new run from the message a failed run answered. Only the run's own person may, while it is their newest run in the thread and nothing works on the thread; otherwise 409, and 404 to anyone who cannot read the thread.

Both streams belong to the session that opened them. Before each write, a stream checks that its session row still exists whenever 15 seconds have passed since its last check (`SessionGuard`); with the heartbeat, no stream writes for longer than that on an ended session. When a session ends on this server (sign-out, a password change, a reset), `SessionRevocations` announces it to that person's open streams once the change has committed: each checks its session before its next write and ends if the session is gone, so nothing written after the announcement reaches it. A session ended by another server on the same database is found by this server's streams at their next check, within 15 seconds plus one heartbeat; until then they may still write. See [08-auth-and-secrets.md](08-auth-and-secrets.md).

`Drover.Client` holds the other end: `DroverApi.WatchRun` reads the stream, reconnects with backoff (half a second, doubling to ten) and the last number it saw, and drops anything at or below that number, so a caller sees every event once and in order. `RunReducer.Apply` turns the events into the `RunView` a screen renders; the events of a run that a call handed work to go into a `DelegationView` under that call, so one stream gives the whole tree. It is a pure function and ignores an event it has already applied, so any prefix of a log followed by the rest ends in the same view as the whole log at once.

Why Server-Sent Events and not a socket library: resume from a cursor is the built-in behavior of the protocol, it is plain HTTP for every proxy and for the MAUI `HttpClient`, and ASP.NET Core and `System.Net.ServerSentEvents` support both ends on .NET 10 and later with no extra package.

### Past tool calls

`GET /api/spaces/{spaceId}/threads/{threadId}/tool-events` returns, as one JSON list of `RunEventResponse`, the `tool.*` and `delegation.*` events that the thread's root runs wrote themselves, and, from the runs those delegations started at any depth, their `tool.*`, `delegation.*`, `message.completed`, and `run.*` status events: each tree's oldest first, the trees oldest first. It is how a conversation shows a finished run's tool calls, and the work it handed to other bots, the way it showed them live: the client feeds each tree's events to `RunReducer` (`ToolHistory.From`), which puts a child's events under the call that handed it the work, as it does for a live stream; a call's id is also the id of the `tool_call` part that made it. It runs under the tenant scope filter and answers 404 to anyone who may not read the thread, an admin of the space included, exactly as reading the thread's messages does; a child's thread has the root thread's audience, so row-level security lets exactly the same people read the child's events. For a thread a delegation made, which has no root run, it returns the same for that thread's own child run: its `tool.*` and `delegation.*` events, and those of the runs below it. It returns the events as stored, so arguments, output, and results are already redacted.

`GET /api/spaces/{spaceId}/threads/{threadId}/export?format=markdown|json` returns the conversation as a file to keep, with `Content-Disposition: attachment` and the bot's name and the day as its file name. Markdown is the default. `ConversationExporter` in `Drover.Engine` reads the thread, its messages, and its tool call rows under the tenant scope filter, so it answers 404 to anyone who may not read the thread, an admin of the space included. It holds what the conversation shows: the person's and the bot's messages in the conversation's order, then any still pending in the order they were sent; each tool call on the bot's message that made it, by the tool's name and its outcome (`succeeded`, `failed`, `denied`, `interrupted`, `awaiting_approval`, `running`, or `unknown`), taken from its row or, when the row is gone, from its result; and each file by name, media type, and size. Tool arguments, tool output, reasoning, and file content are never in it, because they are untrusted text that may hold anything. The JSON form is `ConversationExport` in `Drover.Contracts`, with a `version` field (now 1) that changes when a field is renamed or removed.

## Approval

1. The gate saves the tool call as `pending_approval`. The framework ends the run with an approval request, and the pending state is part of the saved session. The run becomes `waiting` with `awaiting_approval`, and the log holds `tool.approval_requested`.
2. The principal gets a signal on the space's stream and, if they are away, a push (push arrives with notifications).
3. They approve or deny through `POST /api/spaces/{spaceId}/tool-calls/{id}/decision`, with `approved` and an optional `reason`. The route carries the space, like every tenant route. The caller must be able to see the call, and of those who can, must be the run's principal or a space admin. So an admin answers a call in a shared thread, and cannot answer one in a private thread that is not theirs: nobody approves a call whose arguments they cannot read. A member who is not the principal cannot approve what a bot does with another member's authority. A call is answered once; a second answer gets 409.
4. The answer is stored on the `tool_calls` row (`approved`, or `denied` with the reason as the call's result, plus who decided and when) and appended to the log as `tool.approved` or `tool.denied`, in one transaction.
5. The run becomes claimable once every call it waits on is answered. The worker that takes it reads which requests the run last went to wait on from the log, reads their answers from the rows, and runs the agent again with them. Approved: the framework invokes the tool, through the gate, which checks the grant as it stands now and that the tool still has the definition the call was asked for ([06-mcp.md](06-mcp.md)); a tool that changed meanwhile is refused and the model is told to ask again. Denied: the model is told the person declined, with their reason.

The server can restart any number of times between steps 1 and 5. The session that holds the requests is saved again only when the run next stops, so an execution that takes over after a crash hands the same answers over again; the `tool_calls` row then decides what happens, and a call that had started is reported as interrupted, never run a second time.

A call that asks a person to take over the bot's screen (`computer.handover`) waits the same way, with the reason `awaiting_takeover`, and is answered through `POST .../tool-calls/{id}/handback` (done, or could not with a line for the bot) instead of the decision route; see [07-sandbox.md](07-sandbox.md#a-person-takes-over).

A run that ends while a call still waits (it was cancelled) denies that call, with the reason that the run ended.

Not built yet: an approval that nobody answers is meant to expire (default 24 hours) as denied. Until then it waits until it is answered or the run is cancelled.

"Always allow" in the prompt changes the bot's grant from `ask` to `allow` when the user is permitted to do that, so the decision is visible in the bot editor and obeys the hierarchy rule. "Allow for this conversation" uses the framework's own approval rules, which are stored in the session.

## Rules

A bot's rules ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)) are checked at four points of a run, each time against the rules as they are stored at that moment, by the one evaluator in `Drover.Domain.Rules`. No model is asked whether a rule applies. A rule that applies writes `rule.fired`.

| Checkpoint | Where | What each effect does |
| --- | --- | --- |
| `message_arrives` | The executor, for the message that starts a run and the pending ones it takes; the history provider, for a message that steers. Before the model is given it. Every message is checked, also after one was refused, and until the run's first step every message the run holds is checked again, so one that an execution placed and was cut off before checking is not missed. | `block` and `stop` end the run as `failed` with `blocked_by_rule` or `stopped_by_rule`, and mark each refused message `withheld`. |
| `before_tool` | Decided once, on the arguments as the model wrote them, where the model's call is first read (`ReplyRules`); carried out by the tool gate, after the grant. A call the grant refuses is never acted on. | `redact` replaces what matched in the arguments, at once: the event, the stored message, a row made for an approval, the tool, and what the model is shown again all get what is left. `ask` waits for a person. `block` refuses the call and tells the model which rule. `stop` refuses it and ends the run. |
| `after_tool` | The governor, on the tool's result, before it is stored or returned. | `redact` replaces what matched. `block` gives the model an error in place of the result. `stop` does that and ends the run. |
| `before_reply` | A chat-client wrapper in `Drover.Agents`, on each model call's text and reasoning. | `redact` replaces what matched. `block` replaces the reply with a sentence naming the rule. `stop` does that and ends the run. |

**Streamed replies.** While a bot follows any `before_reply` rule, its replies are held back: each model call's answer is collected in full, ruled on, and only then sent on as one chunk. Nothing that a rule removes is ever written to `run_events` or `messages`, and a pattern split across two chunks of the stream is still found. The cost is that such a bot's replies appear whole instead of word by word. A bot with no `before_reply` rule streams exactly as before.

**Asking.** The framework can pause only for a tool that was marked for approval when the agent was built, before any argument is known. So a tool is marked when an `ask` rule's tool conditions cover it, and a rule that asks only for some arguments makes every call of those tools wait for a person. `rule.fired` is written before `tool.approval_requested` when the rule is why; a person's yes is not a second firing.

**Effects hold together.** The strictest applying rule decides, and every applying rule that redacts is carried out as well. A rule that asks never lets through what another rule removes: the person is asked about, and the tool gets, the arguments as the redacting rules leave them. A child's rule therefore cannot undo a parent's.

**A call is decided once.** The decision is made on the arguments as written and kept for the execution under the call's id and its arguments (the same id with other arguments is decided afresh), because after redaction the arguments no longer hold what a rule matched. An execution that takes a call up again after a wait for a person or a restart has only the stored arguments, which are already what the rules left, and decides on those. One consequence is known and accepted: when one rule redacts some text and another blocks on that same text, a call that was waiting for a person is decided on the redacted arguments when it resumes, so it runs, without the text.

**Counting.** `run_exceeds` counts the run's tool calls from its event log as well as from the execution under way, so a wait for a person or a restart does not start the count again.

**Two kinds of condition.** An exact condition is decided on structure and is guaranteed: which tool, the tool's kind, the time of day, a count in the run, and a list on the values of named inputs. A pattern condition looks for something in text and catches the common forms: "contains", a written pattern, a card number, a key, an email address, and a list of addresses with no input named. A pattern condition is useful and is not a guarantee, and the editor, "Try it", and this document say so wherever one is named. Either kind applies when it could not be checked.

**One budget and one cap.** Work is counted, not timed. Every loop that looks at text spends units from the one `RuleBudget` of its evaluation: a unit is a character examined, a piece or an element looked at, an entry of a list compared, or a match kept, and no reader or scanner can be called without the budget. An evaluation gets 50,000,000 units and looks at no more than 500,000 characters and 20,000 pieces of text (strings, numbers, flags, names). Conditions that ask the same thing share one search. An evaluation keeps no more than 100,000 matches over all its searches; each match is charged as it is found, and one more than the ceiling could not be checked. A "contains" condition whose entries read the same (once characters that cannot be seen are set aside and capitals ignored) is refused at save, and one saved earlier is searched for each thing once. Because work is counted, the same input gets the same answer on a busy server and an idle one, with one exception: a written pattern. When the budget is spent, or there is more than the cap, a pattern condition counts as found and an exact one as holding, with the reason in words, and a rule that redacts blocks, because it cannot say what is left. The two-second clock is read between matches, so it stops many slow matches and never one long one. What a written pattern does inside the expression engine during one match cannot be counted in units and cannot be stopped, which is why written patterns are off; see below. Everything else is stopped by units before the clock matters. Looking for text ("contains") is charged what the comparing costs: one reading of the text for each thing looked for, and every character compared at each place the thing could begin. A test runs every condition kind against inputs built to make a scanner work hardest (one character repeated, pairs, deep nesting, many tiny leaves) up to the cap, and fails for a condition kind nobody added to it. "Try it" also refuses a sample longer than 20,000 characters.

**What a pattern condition catches.** Text is compared without characters that cannot be seen (zero-width characters, direction marks, combining marks, controls) and with full-width letters, digits of any script, and unusual spaces in their plain form. A card number is found as cards are written: thirteen to nineteen digits in one piece, or in the groups a card is printed in (four fours, with or without three more, or four, six, five) parted by one space or one hyphen; it has to begin the way a card network's numbers begin, have a length that network uses, and pass Luhn. What was measured: over generated timestamps, date ranges, prices, invoices, phone numbers, coordinates, short ids, rows of numbers, and arithmetic, a test holds false alarms under one in a hundred. Shapes that look like a card run higher and are not in that test: four groups of four digits standing alone, a printed bank account number, a sixteen-digit id, a product or book number, measured in review at two to five in a hundred. Not caught, as stated limits: a card in another spelling (dots, double spaces, tabs, single digits, the four-six-four grouping), a card beside another group of four digits or joined to other digits, a letter of another alphabet that only looks the same, and text split across two inputs or two items of a list. The address list reads each piece once, token by token (tokens are parted by white space, quotes, angle brackets, commas, semicolons, and parentheses): a token with an `@` in it is on the list only when the whole token is one plain address (a mailbox of letters, digits, and `._%+-`, one `@`, a host of letters, digits, full stops, and hyphens) and that address, or its host, is listed; a leading `mailto:` and a closing `.,;:!?` are set aside. Any other token with an `@` is off the list, whatever else in it is listed: a link with an address in its query, `--author=ann@example.org`, a package version, a repository address. Listing a host does not let those through; the address has to stand as a token of its own. Not caught: a percent-encoded `@`, an address split across two items, and what a host written as numbers or in punycode really is.

**Written patterns are off, and why.** A rule can look for text three ways: "contains", the built-in kinds (an email address, a card number, a key), and a pattern a person writes. The third is **off in every preset** (deployment rule `AllowWrittenRulePatterns`, [16-deployment-policies.md](16-deployment-policies.md)), and a server set to turn it on does not start.

The reason, in plain words: Drover cannot stop a written pattern once it starts matching, and some patterns take seconds and gigabytes on a short text. Patterns run on the .NET engine that cannot backtrack. Its documentation says what that is worth: it guards against costly text, not against a costly pattern, and neither it nor a match timeout is a security boundary against a pattern written to be costly. Measured here: `[\s\S]*4[\s\S]{300}Q|Z0` on 2,000 random characters ran 17 seconds and took 4.7 GB with a 250 ms match timeout set, and no timeout was raised. The cause is a wide gap before something specific in a pattern searched for anywhere: the engine builds a new state for almost every character of text that does not repeat. Counted repetition is one way to write that, not the only one (300 dots do the same). Trying a pattern on sample text before trusting it was built and withdrawn: a pattern keyed on a character the sample text did not hold passed it and then cost as much, and the trial itself could be made to run for seconds. A thread cannot be stopped in the middle of a match, so the only real bound is a matcher whose cost is known in advance, or one that runs where it can be killed. Until Drover has one, anyone who may save a rule could stop the server for everyone, so nobody may.

With the rule off:

- A written-pattern condition is refused when a rule is saved, changed, or tried as a draft, with a message that names the deployment rule. The editor does not offer the choice and says why in one line.
- A stored rule that has one is **never built and never run**. Its condition counts as not checked, so the rule takes its effect on everything its other conditions allow (a rule that removes text blocks), and its read-back says so.
- "Contains" and the built-in kinds stay. The built-in expressions are Drover's own and always run.

The code that would run a written pattern is still there for the day the rule can be turned on, with what can be read off a pattern refused at save: constructs that need backtracking, a count over 256, a pattern the engine says is too large to build. That is not enough to make it safe, and the documentation of the rule says so. At most 256 written patterns are kept ready, the one used longest ago going first.

**A match that fails.** A match that runs out of the engine's one-second timeout, or of memory, ends the evaluation's budget: nothing more is checked, every rule not yet decided counts as not checked, and a rule that removes text blocks. A reply is never let through as written after a search of it failed.

**What a rule cannot rewrite.** A rule that redacts rewrites text: a reply, a message, a string in a tool's input or result. The name of an input, a number, and a flag are examined and never rewritten, because the result would not be the call the tool knows; neither is an object in which two inputs share a name, because writing it out again would keep only one. A redacting rule that matches in one of those, or that could not check its subject for work or for size, blocks, and nothing of the subject is kept in what is stored. A rule that blocks on what stands in the name of an input keeps nothing of the call either.

## Conversation summaries

A person's main thread with a bot grows without end, so the server keeps a running summary of it on the thread's own row (`summary`, `summary_through_position`, `summary_updated_at`; [02-data-model.md](02-data-model.md)). `ConversationSummarizer` in `Drover.Engine` writes it.

**When.** After a root run on a main thread **completes**, in the worker that ran it, once the run's status, its `run.completed` event, and the signal to its person have all been committed and published. So the person sees the reply finished and the thread free at the moment they would without summaries, and a message they send next is answered by the next run as usual (it may be claimed by another worker while the summary is written). A run that waits, fails, or is cancelled is not followed by one. The summary does hold the worker's slot until it is written or fails; `MaxConversationSummarySeconds` bounds that. Inside the run's lifecycle was the other choice and was rejected: the run would stay `running` for the length of another model call after its reply was done, and a message sent meanwhile would wait behind it.

**Whether.** A summary is due when at least `Runs:SummaryAfterMessages` (default 20) person and bot messages lie beyond `summary_through_position`; tool messages and withheld messages do not count. The newest `Runs:SummaryKeepsRecentMessages` (default 6) of them always stay out, so a summary never reaches into the exchange still going on; it covers every message from the last summary's end up to the one before the first message it keeps out. One summary takes in at most 200 person and bot messages; a thread further behind is caught up a part at a time over the runs that follow. Side chats never get one. With the deployment rule `AllowConversationSummaries` off nothing is written ([16-deployment-policies.md](16-deployment-policies.md)).

**How.** In one short transaction as the run's person, the summarizer reads the thread, its conversation, the bot, its model connection and key, the catalog prices, and the space's effective policy, then closes it. The bot's own model on the bot's own connection is called once, with no tenant transaction open (it refuses to run inside one), under the space's rule on private addresses, with at most 1,000 output tokens and within `MaxConversationSummarySeconds`. The request holds a fixed instruction, written by Drover, and one user message with the summary so far and the new messages between markers, angle brackets written out so no message can close a marker. The instruction says that everything between the markers is material to summarize and nothing in it is an instruction. Only the text parts of person and bot messages are sent, each cut at 4,000 characters, with one line naming each tool the bot called; reasoning, tool arguments, tool results, and withheld messages never are, and the text passes the run's redactor (the connection's key and key-shaped tokens) on the way out and the answer on the way back. The answer is cut at 8,000 characters.

**Storing.** One transaction records the call in `usage_records` against the run that prompted it (its run, root run, bot, connection, model, tokens, and cost at the catalog's prices) and replaces the summary. The text, the new reach, and the time are written by one `UPDATE` that also requires the reach to be what was read, so a crash leaves the old summary or the new one and never half of each, and a summary written meanwhile by another worker is never overwritten by an older one. The run's own `usage` totals and its event log are closed by then and are not changed: the summary's cost is the `usage_records` row.

**Failure is quiet.** A model that fails, times out, or answers nothing, an unreadable key, or a database error is logged as a warning; the old summary stays, the run keeps the outcome it already had, and the next run that completes on the thread tries again. A server that stops cancels the call and writes nothing.

**Using it.** When the bot's `memory_settings.compaction` is `summarization` and its main thread has a summary, `PostgresChatHistoryProvider` gives the model, before every model call, a first user message labeled as the summary of the first N messages and saying it is not an instruction, then only the messages placed after `summary_through_position`, in call order; a tool result whose call the summary covered is left out with it. The summary is read from the thread's row in the same transaction as the history, never from the framework's session, so a run picks up a summary written while it works. Every other compaction value, and every side chat, gets the whole conversation as before.

## Delegation

Delegation is the built-in tool `delegate`, with two arguments: `bot_id`, a bot directly beneath the caller, and `task`, written in full because the child sees nothing else. Its description, as the model sees it, lists those bots with each one's `description_for_bots`, and marks a paused one. Like every built-in tool it is a catalog row, off until granted and narrowed by every grant above; its sensitivity is `write`, so a tainted run asks before it hands work on.

The call passes the tool gate like any other. Then the child bot is exposed to its parent as a function, the framework's agent-as-tool (`AsAIFunction`), wrapped in `GovernedAgent`, and invoked with the task. The wrapper asks the governor to begin a child run. In one transaction, as the run's person, the governor:

1. Checks the caller is active and holds a grant for `delegate`, and that the target is allowed ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)): directly beneath the caller and active. A paused or removed target is refused with a result that says so.
2. **Loop check.** Walks the caller's run ancestors. If the target bot already appears in that chain, refuses with `loop_detected`.
3. **Budget check.** The root's row is locked first, so two delegations in one tree take turns. Refuses with `budget_exceeded` if the tree would exceed the space's maximum delegation depth (default 5), maximum runs per tree (default 50), or cost budget per tree. Each is a deployment ceiling a space may only lower (`MaxDelegationDepth`, `MaxRunsPerDelegationTree`, `MaxDelegationTreeCostCents`, [16-deployment-policies.md](16-deployment-policies.md)); the cost is what every model call of the tree has recorded so far. A refusal's result names the limit and its value.
4. Creates a thread for the child (origin `delegation`, the root thread's owner and audience, so exactly the people who can see the root thread can see it), a user-role message holding the task (authored by the calling bot, redacted of the run's secrets like any stored tool argument), and a child run with `trigger` `delegation`, `parent_run_id`, `root_run_id`, `parent_tool_call_id`, `depth + 1`, the same `principal_user_id`, and the parent's taint.
5. Saves the parent's tool call with `child_run_id`, and appends `delegation.started` to the parent's events.

A refusal is the call's result, as `{"error": ..., "reason": ...}` with `reason` one of `no_grant`, `not_beneath`, `target_unavailable`, `loop_detected`, `budget_exceeded`, and for the last two of those a `detail`: `paused` or `removed` for the target, `depth`, `runs`, `handoffs` (the calling bot's own limit on one run, checked after the tree's `runs`), or `cost` for the limit, so a screen can say which in its own words. No child run is begun.

The child then runs as its own agent with **its own** grants, history, session, and events, inside the parent's tool call. The wrapper's inner agent (`ChildRunAgent`) asks the governor to execute the child run, and the engine executes it the way it executes any run: loaded as its person, its framework agent built from its own bot, its events written to the tree's log with its own run id, its status ended in one transaction with its usage. When it ends, the governor turns what it came to into the parent's tool result: its last reply when it completed, what went wrong when it failed, and a "cancelled" result when it was cancelled while the parent went on. The result passes the parent's `after_tool` rules like any tool's, and the parent's log gets `delegation.completed` and then `tool.completed`.

Taint goes both ways. A child starts tainted when its parent is. A child that read text from an untrusted source, itself or through a run beneath it, taints its parent when it reports back, whatever it came to, before the parent sees its answer: the parent's agent is built again before its next model call, and its consequential tools ask from then on ([06-mcp.md](06-mcp.md)). Without this, a child's answer would carry a stranger's text to a parent whose writes run unattended.

`BeginChildRun` is idempotent on the parent's tool call id. If the server restarts while a child is working, the parent's call is cut off with its row `running`; like a read-only call it is asked again (through the gate) when the parent's history is read, the governor returns the existing child run, and the child continues from its own saved history instead of starting over. A child that had already ended is not run again: its result is read from its rows.

Asking again does not undo what was decided. A call a person approved goes on without asking twice (its approval is handed over again and it comes back through the gate), and so does a delegation whose child has begun, even when its grant is `ask` or the parent has since been tainted; a grant that no longer allows the call still refuses it. The parent saved no session when it stopped for its child, so the session it resumes from can still hold the delegation's old approval request, which the framework answers as rejected. Such a framework-made result, for a call whose row is `running` and which is to be asked again, is dropped where the history is read and is not taken as the call's end in the log: the call is asked again and its own result ends it.

If a child needs an approval, the whole tree pauses on it: the child's request is in the tree's log, its session is saved, the child becomes `waiting`, and each run above it stops where it is and becomes `waiting` with `awaiting_approval`, its delegation call left open (its log has `tool.requested` and `delegation.started` and no end yet). The root's waiting signal goes to its person. The root is claimable once every request in its tree is answered (`sys.claim_runs` already looks at the whole tree); the decision endpoint answers a child's call like any other, after locking the root. When the root is taken up again, its delegation is asked again and resolves to the same child, which resumes with the answer.

A lease is held on the root run and covers its tree. Child runs are rows with their own status, usage, and events, executed by the worker that holds the root. Every write a child makes is fenced by the root's claim, and a child's end locks the root's row first. Each child's row is watched while it works, so a cancel aimed at the child alone reaches it.

### Teams

Beyond one-to-one delegation, a parent bot can lead its children with one of the framework's orchestration patterns: sequential, concurrent, handoff, group chat, or a planner-led pattern. The pattern is data on the parent bot. A team run is a framework workflow:

- Each member is wrapped in `GovernedAgent`, so each still gets its own child run, grants, and events.
- Checkpoints are saved to PostgreSQL at each workflow step. After a restart the workflow resumes from the latest checkpoint.
- A workflow's request for human input surfaces as the same approval prompt and is re-raised after a resume.

Because every run in the tree shares `root_run_id`:

- The activity view shows the whole chain from one query and one stream.
- **Cost rolls up.** Each `usage_records` row carries `root_run_id`. When a run finishes (completed, failed, or cancelled; not when it waits), its usage is added to `subtree_usage` on itself and on each ancestor run, so a run's `subtree_usage` is its own usage and that of every finished run beneath it. Per-bot totals are sums over `usage_records`; "this bot and everything it started" is a sum over its runs' `subtree_usage`. A call on a model whose price the catalog does not know is recorded with its tokens and no `cost` (null, never zero), so a total says what it leaves out. A period's figures are summed in the currency most of its priced calls used; calls priced in another currency are named beside the total, not added to it. The usage page (`GET /api/spaces/{spaceId}/usage`) reads `usage_records` for a period that starts at midnight in the reader's time zone: per bot alone, and with the bots beneath it in the crew, each one sum per bot built into the tree in memory. Choosing a bot lists its most expensive runs (`/usage/bots/{botId}/runs`) among those whose thread the reader can see; the runs in threads they cannot see are one total, with no thread, time, or id. `GET /runs/{runId}/usage` gives one run and the runs beneath it, for the line under a finished reply, only to a reader who can see its thread. Owners and admins read the whole space; a member reads the runs that act for them, scheduled ones included.
- **Cancel a subtree.** `POST /api/spaces/{spaceId}/runs/{id}/cancel` sets `cancel_requested_at` on that run and every descendant run in one statement. The route carries the space, like every tenant route: a run cannot be found without knowing its tenant. The run's own principal may cancel, and so may a space admin. The worker executing the run is told at once when it is on the same server, and otherwise sees the flag on its next look at the run row (once a second); it cancels the model call, tool, or workflow, and writes `run.cancelled`. Queued and waiting runs are cancelled directly by the endpoint when the caller can see the run's thread. An admin who cannot see a private thread cannot write its events, so a worker ends the run for them, acting as the run's own principal: a cancel request makes a waiting run claimable at once, without waiting for an answer to its approval, and a queued run is claimable anyway. The parent of a cancelled child receives a "cancelled" tool result and continues, unless it was cancelled too; a child stopped because its parent was cancelled ends with `cancelled_by_parent`. Cancelling a child that waits for a person denies the approvals it and the runs beneath it wait for, in the same transaction, so the tree is claimable again: the child then ends as cancelled and its parent goes on. A root that ends for any reason also ends every run beneath it that is still open, as `cancelled_by_parent`, with their waiting approvals denied, in the transaction that ends the root.

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
   | Tool call approved and `running` when the process died | Mark it `interrupted`. The stale session makes the framework reject the approval on the next run; the `tool_calls` row wins over that result, so the model is told "interrupted, outcome unknown", not "rejected". A read-only call, a delegation, and an idempotent built-in tool (`sandbox.share_file`) are the exception, as above: the approval is handed over again and the call runs again (a delegation resolves to its child run, a share finds its file), without asking a second time. |
   | A delegation was in progress | The delegation is requested again and resolves to the existing child run, which continues |
   | A team workflow was in progress | Resume from its latest checkpoint |

4. After three attempts the run fails with `lease_lost`.

The engine also repairs the event log when it takes a run over, from what the rows say. A step whose model call was cut off gets `step.discarded`, or `message.completed` if its message did reach the thread. A tool call the log never saw finish gets `tool.completed` with its row's outcome, or `interrupted`; a read-only or idempotent one that was running is left open, because it runs again.

Drover never silently repeats an action that might have already happened. That is the honest limit of at-least-once execution, and the activity view shows `interrupted` so the human can see it.

Connected clients see the stream drop, reconnect with their last `seq`, and continue.

## Schedules

A schedule row holds a cron expression, a time zone, a prompt, a thread mode, and `next_run_at`, the next time it is due in UTC. `ScheduleService` computes `next_run_at` when the schedule is saved and refuses a schedule that breaks a deployment rule ([16-deployment-policies.md](16-deployment-policies.md)): times closer together than `MinScheduleIntervalMinutes`, or more schedules in the space than `MaxSchedulesPerSpace` (counted under a per-space lock, so two additions count one after the other).

**Whose schedule it is.** A schedule runs as the person who made it: with their notes, their own sign-ins to tool servers, in their private conversation, and its message is stored as theirs. So only that person may change what it sends or when (prompt, cron, time zone, thread mode) or turn it back on. Another admin may turn it off or remove it, and is told so in a sentence when they try anything else. An owner whose role is lowered keeps their schedules, and each keeps running with that person's own, now lower, authority, because a run's grants and access are always its principal's: a schedule never gives anyone more than its owner has.

**The loop.** `ScheduleLoop` is a hosted service in `Drover.Server`; every server runs one. It calls `ScheduleRunner.SendDue` in `Drover.Engine` when the server starts and every 30 seconds after.

1. **Claim.** In a transaction with no tenant, call `sys.claim_due_schedules(now, claim_until, limit)`; the function checks both moments against the database's own clock ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)). It selects enabled schedules whose `next_run_at` has passed, `FOR UPDATE SKIP LOCKED`, sets their `next_run_at` to `claim_until` (five minutes ahead), and returns only `(space_id, schedule_id, owner_user_id)`, in one statement. A second server asking at the same moment skips the locked rows and, once they commit, no longer sees them as due. PostgreSQL cannot read cron, which is why the function moves `next_run_at` to a claim rather than to the schedule's next time.
2. **Enter scope.** For each, open a tenant scope for that space with the schedule's owner as the person (`ScheduleFiring`). Lock the schedule row. If `next_run_at` no longer equals this pass's claim, the schedule was changed, turned off, removed, or claimed again by another server since, and nothing is done.
3. **Decide.** If the owner is no longer a member of the space, or the bot was removed, nothing is sent and the schedule is turned off (`enabled = false`, no `next_run_at`): it could never send again. If the bot is paused, nothing is sent and the schedule stays on. Otherwise:
4. **Send.** Store the prompt as a user message whose author is the owner and whose `schedule_id` names the schedule, and queue its run, exactly as [From message to run](#from-message-to-run) does: under the thread's lock, in the same transaction, with `run.queued` and the `NOTIFY`. The run's `trigger` is `schedule` and its principal the owner. The conversation is the thread mode's:

   | `thread_mode` | Conversation |
   | --- | --- |
   | `new_thread` | A new thread each time, private to the owner, `origin = schedule`, named after the prompt |
   | `same_thread` | The owner's main thread with the bot ([Conversation summaries](#conversation-summaries) and [02-data-model.md](02-data-model.md)), made then if they have none |

   When that thread already has a run under way, the message is stored pending and waits (as `whenBusy = queue`): when the run ends, it starts the next run, and `ThreadLine` gives a run started by a schedule's message the trigger `schedule` too.
5. **Record.** In the same transaction write `last_run_at`, `last_outcome` (`started`, `waiting_for_reply`, `bot_paused`, `bot_removed`, or `owner_left`), `last_run_id` when a run was queued, and the real `next_run_at`: the schedule's first time after now. Counting from now, not from the time it was due, is what makes a server that was down across several due times send the schedule once, not once for each.
6. **Commit, then signal** the workers and the owner's open screens.

One schedule that fails to send does not stop the pass. Its row keeps the claim, so it is due again when the claim runs out and is tried again then, never lost and never sent twice for one due time. A person who changes a schedule while it is claimed replaces the claim with the schedule's new time, so the claimed send is dropped.

**What people see.** The message is the owner's for every purpose a run has (whose steering it takes, whose pending messages it answers), and is marked as not typed by them: `MessageResponse.scheduleId`, with `scheduleCron` while the schedule exists, and the conversation shows "Sent by the routine 'Every weekday at 09:30'" under it, or that the routine has since been removed. A schedule's read contract carries `lastRunAt` and `lastOutcome`. For a run it started, `lastOutcome` is that run's status now (`queued` through `completed` or `failed`), with `lastRunId`, `lastThreadId`, and `lastReason` (the run's error in words), but only for a caller who can see that run's conversation: runs have no audience of their own, so `RunRepository.ListVisibleGlances` joins each to its thread, which row-level security hides when it is another person's private conversation. Anyone else, another admin included, reads `started` and nothing from the conversation. The status page has a Scheduler row: working while this server looked for due schedules in the last 90 seconds, otherwise when it last looked.

**Running once now.** `POST /bots/{botId}/schedules/{scheduleId}/run-once` (owners and admins) sends the schedule's prompt once, now, through `ScheduleFiring.RunOnce`: the same send as step 4, in its own tenant scope, but for the person who asked, never the owner. The message is theirs, marked with the schedule, in a new thread private to them or their own main thread as the thread mode says, and the run's principal is them, so it reads their notes and sign-ins, not the owner's. The schedule row is not touched: its `next_run_at`, its claim, and its `last_*` stay as they were. A paused bot refuses with a sentence. The prompt was written by an owner or admin of the space, like the bot's instructions, so the run starts untainted like any run a person starts.

**Checking a time.** `POST /bots/{botId}/schedules/check` answers what saving a cron expression and time zone would come to, without saving: the sentence a save would refuse it with (a malformed expression, an unknown zone, times closer than the deployment allows), or its next three times. The editor uses it to explain an expression it cannot put in words before anything is saved.

From there a scheduled run is an ordinary run. It acts for the owner, so it recalls the bot's notes about them (its conversation is private to them), and its replies count as new for them until they open the conversation. It starts untainted and is tainted the way any run is, by a tainted note it is given or a file its conversation carries.
