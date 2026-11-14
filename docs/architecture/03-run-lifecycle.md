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
- **A message a rule withheld is the exception.** It is marked `withheld`, gets its position when the run ends so people still read it where it was sent, and starts no run.
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
| `rule.fired` | A rule of the bot applied: rule id and name, checkpoint, effect, a sentence saying why, and the tool call id when there is one. Never the text that matched. |
| `tool.approved`, `tool.denied` | Tool call id, who decided. Written by the decision endpoint, while the run waits. |
| `tool.started`, `tool.output`, `tool.completed` | Tool call id, incremental output for long commands, outcome (`completed`, `failed`, `refused`, `interrupted`, `cancelled`), redacted result preview, duration |
| `delegation.started`, `delegation.completed` | Tool call id, child run id, child bot id, result summary |
| `usage.recorded` | Tokens and cost for one model call |

Tool activity is not a side channel. The activity view is a projection of these events plus the `tool_calls` rows.

The log closes what it opens. Every `step.started` is followed by that step's `message.completed` or by `step.discarded`, and every `tool.requested` by a `tool.completed` or a `tool.approval_requested`, whether the run finished, failed, was cancelled, or was taken over after a crash. A client that replays the log therefore shows what a reload of the thread would show. Payload records and type names are in `Drover.Contracts.Runs`; both sides read and write them through `RunEventTypes.Read` and `RunEventTypes.Write`. A contract carries free-form JSON (an event's data, a tool's arguments and result) as JSON text, never as a JSON type: the check that no contract can carry a secret reads every property a contract reaches, and a JSON type reaches everything.

## Streaming and resume

`GET /api/spaces/{spaceId}/runs/{rootRunId}/events?after={seq}` returns Server-Sent Events. The route carries the space, like every tenant route. Each frame's `id` is the `seq`, its `event` is the type, and its `data` is the event as `RunEventResponse`.

1. The server checks, in a short transaction as the caller, that the run is a root run of the space and that the caller may see its thread. Row-level security answers both: another member asking for a private thread's stream gets 404, and so does anyone outside the space.
2. It subscribes to the live feed, then replays stored events with `seq > after` in pages of 200, each page in its own short transaction. No transaction lives as long as a stream.
3. When it reaches the end, it switches to the live feed. Because events are published after commit and are gapless, the handover check is a simple "next seq is last + 1"; on a gap it reads the missing rows from the table.
4. When nothing arrives for 15 seconds (`Runs:HeartbeatSeconds`) it sends a comment line, so proxies keep the stream open, and reads the table again. That read is also how a watcher on one server sees events another server wrote.
5. When the root run is terminal and everything is sent, the server closes the stream. A client that had already seen everything gets a stream that closes at once. The server also closes its streams when it shuts down.

On reconnect the client sends `Last-Event-ID`, or `?after=`; the header wins when both are present. The phone locking, the tab closing, or the server restarting all look the same to the client: reconnect with the last number it saw.

A second, lightweight stream, `GET /api/spaces/{spaceId}/stream`, carries non-durable signals for the current user in that space: `thread.changed`, `approval.waiting`, `run.finished`, each naming the thread and the run. It has no replay and is delivered only by the server that made the change. On reconnect the client refetches lists; the client library says `reconnected` itself each time it has to connect again. This keeps durable replay for the one thing that needs it.

`Drover.Client` holds the other end: `DroverApi.WatchRun` reads the stream, reconnects with backoff (half a second, doubling to ten) and the last number it saw, and drops anything at or below that number, so a caller sees every event once and in order. `RunReducer.Apply` turns the events into the `RunView` a screen renders. It is a pure function and ignores an event it has already applied, so any prefix of a log followed by the rest ends in the same view as the whole log at once.

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
