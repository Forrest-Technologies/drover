# 7. Sandboxes

A sandbox is an isolated computer a bot can work in: files, processes, network. Phase 1.

## The interface

As built, in `src/Drover.Domain/Sandboxes/`:

```csharp
namespace Drover.Domain.Sandboxes;

public interface ISandboxProvider
{
    string Kind { get; }

    Task<SandboxHandle> Create(SandboxSpec spec, CancellationToken cancellationToken);

    Task<SandboxInfo> Describe(SandboxHandle sandbox, CancellationToken cancellationToken);

    Task KeepAlive(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken);

    Task Pause(SandboxHandle sandbox, CancellationToken cancellationToken);

    Task<SandboxHandle> Resume(SandboxHandle sandbox, TimeSpan idle, CancellationToken cancellationToken);

    Task Destroy(SandboxHandle sandbox, CancellationToken cancellationToken);

    IAsyncEnumerable<CommandOutput> Run(
        SandboxHandle sandbox,
        CommandRequest command,
        CancellationToken cancellationToken);

    Task<Stream> ReadFile(SandboxHandle sandbox, string path, CancellationToken cancellationToken);

    Task WriteFile(SandboxHandle sandbox, string path, Stream content, CancellationToken cancellationToken);

    Task<IReadOnlyList<SandboxEntry>> ListFiles(
        SandboxHandle sandbox,
        string path,
        CancellationToken cancellationToken);

    Task SetNetworkPolicy(SandboxHandle sandbox, NetworkPolicy policy, CancellationToken cancellationToken);
}


public sealed record SandboxSpec
{
    public required Guid SpaceId { get; init; }

    public required Guid SharingGroupId { get; init; }      // the owner bot's id

    public required Guid IdempotencyKey { get; init; }      // the sandbox row's id

    public required NetworkPolicy Network { get; init; }

    public string? Image { get; init; }                     // null: the operator's default

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public IReadOnlyDictionary<string, string> Environment { get; init; }
}


public sealed record SandboxHandle(string ProviderSandboxId, Guid SpaceId, Guid SharingGroupId, string AccessToken);

public sealed record SandboxInfo(ProviderSandboxState State, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset? EndsAt);

public sealed record CommandRequest { Program, Arguments, WorkingDirectory, Environment, Timeout; static Shell(string) }

public abstract record CommandOutput;   // CommandStarted(Pid), CommandStdout(Data), CommandStderr(Data), CommandExited(ExitCode, Error)

public sealed record SandboxEntry(string Name, string Path, SandboxEntryKind Kind, long Size, DateTimeOffset? ModifiedAt);

public sealed record NetworkPolicy(NetworkAccess Access, IReadOnlyList<string> AllowedHosts);
```

All types are Drover's. Failures are `SandboxProviderException` with a `SandboxFailure` (`InvalidKey`, `NotFound`, `HandleMismatch`, `Unauthorized`, `RetryLater`, `RateLimited`, `QuotaExceeded`, `Rejected`, `TooLarge`, `TimedOut`, `Unavailable`). Its message is fixed text: it never holds a key, a token, or the provider's own wording.

- **The handle** is a capability: it carries the per-sandbox access token, and only the broker builds one. Its printed form leaves the token out. It carries the space as well as the group, so the adapter can check both against the provider's record.
- **The sharing group id is the owner bot's id.** For `private` and `named` that is the bot itself; for `subtree` it is the highest ancestor whose policy is `subtree`. No extra column is needed.
- **`Describe`** returns the provider's own record (`Running`, `Paused`, or `Gone`, its metadata, and when it will pause), kept apart from the row's `SandboxState`.
- **`KeepAlive`** exists because no provider detects idleness for Drover (see the E2B facts below). The broker calls it on each use.
- **`Resume`** returns a handle, because a provider may issue a new token when it resumes.
- **`Run` takes a program and an argument list**, not a command line. Arguments are never parsed by a shell, so text from a model cannot inject a second command. `CommandRequest.Shell` builds a `bash -l -c` line for text Drover itself writes. `Run` streams output so a long command shows live in the activity view. Stopping early, by cancellation or by not reading to the end, kills the process; a command still running at its `Timeout` is killed and fails with `TimedOut`.
- **`Destroy`** of a sandbox already gone is not an error.

### Conformance suite

`tests/Drover.Adapters.Conformance/Sandboxes/SandboxProviderConformance.cs`. The scenarios are data (`Scenarios`, run by `RunScenario`), because an inherited fact cannot be made explicit for a live run and pull request CI does not filter by trait. Each adapter runs the same theory offline against recorded answers and live, explicitly, against the real service. The scenarios:

| Scenario | What it shows |
| --- | --- |
| `creates_with_metadata` | The handle carries the spec's space and group; the provider's record carries `drover.space` and `drover.group`; the printed handle has no token |
| `keeps_alive` | A keep-alive moves the provider's pause time later |
| `runs_a_command` | Output arrives in several pieces before the exit; stdout, stderr, and a non-zero exit code are kept apart |
| `exits_zero` | Exit code 0 (which the E2B wire leaves out) is reported as 0 |
| `round_trips_binary` | A file with a NUL and non-UTF-8 bytes reads back identical |
| `lists_files` | A listing has names, kinds, sizes, and times |
| `denies_network` | A command reaches the internet, then cannot once the network is set to off |
| `pauses_and_resumes` | A paused sandbox reports paused, and a file survives pause and resume |
| `destroys` | After destroy the provider reports `Gone`, and a second destroy is fine |
| `refuses_another_groups_handle` | Every operation with another group's or space's handle fails with `HandleMismatch`, sends nothing but reads, and leaves the sandbox untouched |
| `refuses_a_wrong_token` | The sandbox refuses a wrong token with `Unauthorized`; no message contains either token |
| `kills_on_cancel` | Cancelling a running command kills it |
| `stops_at_the_time_limit` | A command past its time limit fails with `TimedOut` and is gone |

Every scenario also checks that no vendor type leaves the adapter.

## The broker

Bots never hold a sandbox. `SandboxBroker` in `Drover.Engine` (`src/Drover.Engine/Sandboxes/`) is the only caller of `ISandboxProvider` for a bot's work; the only other caller is the sweep ([Retiring and the sweep](#retiring-and-the-sweep)), which only pauses and destroys. Both are registered, with the five sandbox tools, only on a server whose operator configured a provider key (below); a server without one has no sandbox tools in its catalog at all.

For each sandbox tool call it:

1. **Finds the sandbox, in one short tenant transaction** as the run's person: loads the calling bot, refuses if its sandbox policy is `none`, resolves its sharing group from its path to the root (`SandboxGroups.OwnerOf`, a pure function in `Drover.Domain`), and, when the call names another bot in `environment`, checks the caller may work there (`SandboxGroups.MayEnter`). It loads the owner's live row (`creating`, `running` or `paused`), or inserts a `creating` one, and reveals the access token from the vault. The transaction closes before any provider call. A partial unique index (`ux_sandboxes_one_live_per_owner`, migration `SandboxPerGroup`) allows one live row per owner, so two first uses at once make one row: the loser's insert fails, and it reads the winner's row.
2. **Makes it ready, with no transaction open.** A new row: `Create` with the row's id as the idempotency key, then a second short transaction stores the token with `ISecretVault.Store(SecretKind.SandboxToken, ...)` as a space secret (every member's runs use the bot's sandbox), sets the provider's id, and marks the row `running`. A stored row: `Resume` with the idle time. Resuming a running sandbox only keeps it longer, and the provider pauses a sandbox on its own, so the row cannot say which it is; one call covers both. A new token from `Resume` replaces the old one in the vault. A sandbox the provider no longer has is marked `destroyed`, its token deleted, and a new one made once; the tool's result then says its earlier files are gone.
3. **Applies the network rules** when they differ from the last ones it set on that sandbox (kept in memory, so after a restart they are set once again). A change to a bot's network setting reaches its running sandbox on the next use.
4. **Runs the operation.** A command streams its output into the run as `tool.output` events ([03-run-lifecycle.md](03-run-lifecycle.md)) and returns exit code, stdout and stderr, each cut to half the run's tool result limit. A command's own limit keeps the sandbox up for at least that long plus a minute, and afterwards the sandbox goes back to its idle time.
5. **Records the use** in a last short transaction: the row's `last_used_at`, `expires_at` and state, and the tool call's `sandbox_id`. When the sandbox's network is not `off`, or the sandbox is `tainted` because an attached file was copied in, reading its output (a command, a file, a listing) taints the run, so later consequential tools ask first ([06-mcp.md](06-mcp.md)).

The access token goes from the vault into the handle and nowhere else. Output is scrubbed of it before any event or result, including a token split across two pieces of output (the tail that could start one is held back until more arrives); the run's own redactor applies on top. It is never logged, put in an exception, a tool argument, or a run event. A provider failure reaches the model as Drover's own sentence, never the provider's text.

Limits come from the deployment policy and only narrow ([16-deployment-policies.md](16-deployment-policies.md)): a command defaults to 60 seconds and runs at most `MaxSandboxCommandSeconds` whatever the bot asks; a file read or written is at most `MaxSandboxFileBytes`; the idle time is the bot's setting, or 15 minutes, never above `MaxSandboxIdleMinutes`; and a bot set to open network is refused where `AllowOpenSandboxNetwork` is off. `BotService` refuses to save an idle time or a network setting past them.

### Operator key

The operator's E2B key is server configuration, not a secret in the vault: the vault is per space under row-level security, and this key belongs to the whole server. It is read at start from `Drover:Sandbox:E2B:ApiKey` (environment `Drover__Sandbox__E2B__ApiKey`) or from the file named by `Drover:Sandbox:E2B:ApiKeyFile` (a container secret, the same pattern as `Database:AppPasswordFile`), and held only in the adapter's options in memory. It is never written to the database or the vault, never returned by an endpoint, and never logged. Optional: `Drover:Sandbox:E2B:Domain` (default `e2b.app`), `Drover:Sandbox:E2B:Template` (default `base`), and `Drover:Sandbox:E2B:DesktopTemplate` (default `desktop`, the template a sandbox with a screen starts from; see [Computer use](#computer-use)).

- No key: no provider, no sandbox tools, and not an error. The bot editor's Sandbox section says no provider is set up, and the status page's `Sandboxes` row says "Not configured".
- A key without the `e2b_` prefix, both settings at once, or a key file that is not there stops the server at start, naming the setting and never the value.
- The status page shows only "configured" or "not configured", and names no provider. `GET /api/spaces/{spaceId}/bots/{botId}/sandbox/options` tells the editor whether a provider exists, whether open network is allowed, and the most idle minutes.

`SecretKind.SandboxProviderKey` stays reserved for a future hosted service where a space brings its own provider key.

Not built yet: `sandbox.enter`, and the terminal view in the activity screen. The E2B clients go through the server's checked outbound handler, which does not use an outbound proxy.

## Sharing policies

| Policy | Who shares one environment |
| --- | --- |
| `private` (default) | The bot alone |
| `subtree` | The bot and all its descendants |
| `named` | The bot and the bots listed in `bot_sandbox_shares` |

A **sharing group** is the set of bots that share one environment. Each group has an id; a private bot is a group of one.

### Enforcement at the adapter, not only in application code

The handoff asks that the jail not rest on an application check alone. Four mechanisms, from strongest to weakest:

| # | Mechanism | What it guarantees |
| --- | --- | --- |
| 1 | **One provider sandbox per sharing group.** Drover never puts two groups in one sandbox and separates them with directories or users. | The boundary between a private bot and everything else is the provider's virtual machine boundary. No Drover code path can read across it, because no such path exists inside one machine. |
| 2 | **A per-sandbox access token**, issued by the provider at creation and stored in the vault as a space secret. The adapter cannot enter a sandbox (run a command, read or write a file) without it. | Knowing or guessing another sandbox's id is not enough to get inside it. A bug that passes the wrong token gets an authentication failure from the sandbox. At E2B this guards the in-sandbox daemon only: the control plane (pause, kill, network, time to live) needs just the API key and an id, and it hands the token to anyone holding the key. Mechanism 3 is what guards those calls. |
| 3 | **Provider-side metadata check.** Each sandbox is created with metadata `drover.space` and `drover.group`. Before every operation except creation, the adapter reads the sandbox's record from the provider and refuses with `HandleMismatch` if either value differs from the handle. | The check is against the provider's record, not Drover's database, so a corrupted or confused row cannot redirect a bot into another group's machine, through the daemon or the control plane. It costs one read per operation. |
| 4 | **Network policy at the provider.** Egress follows the bot's sandbox settings: off, allowlist, or open. Open still denies the private, carrier-grade NAT, and link-local (cloud metadata) ranges. | A process in a sandbox cannot reach the Drover server's internal addresses through the provider's network. Whether sandboxes can reach each other is not documented by E2B, so Drover does not rely on it; the deny rules above are what it sets. |

The application-level policy check in the broker is the fifth layer, and the row-level security on `sandboxes` and `secrets` is the sixth.

Inside a shared sandbox, bots are not separated from each other. Sharing means sharing. The UI says so when the policy is chosen.

Provider accounts: on a self-hosted server each space uses the operator's key. On the hosted service, a space can bring its own provider key, which separates tenants at the provider account level; otherwise mechanisms 1 to 4 apply under the platform's key.

### Working in another bot's environment

`sandbox.run` and the file tools take an optional `environment` argument: the handle of another bot. The broker resolves that bot's sharing group and allows the call when the caller is in that group, or the group's owner shares by name and lists the caller. A `named` member's own sandbox is its own; it reaches the shared one only by naming the owner. In a `subtree` share the highest bot on the path whose policy is `subtree` owns the sandbox, whatever the bots below it say for themselves. Siblings cannot reach each other's sandboxes, and neither can a parent reach a child's: the `sandbox.enter` grant that would let an ancestor direct a child's work there is not built yet (it waits for delegation).

### When a bot is removed

| Removed bot is | Policy | What happens |
| --- | --- | --- |
| Owner | `private` | Sandbox is retired |
| Owner | `subtree` | Its subtree is removed with it. Sandbox is retired. |
| Owner | `named` | Sandbox is retired. Other members lose access. |
| A member, not the owner | `subtree` or `named` | Its processes are stopped. The sandbox continues for the rest. The access token is rotated by recreating the handle, so nothing the removed bot started keeps a way in. |

"Retired" means: paused immediately, state `retiring`, kept for seven days so a human can download files or assign the environment to another bot, then destroyed. The retention period is a space setting and can be zero.

If a human moves a child out of a subtree before removing the parent, the child leaves the sharing group and gets its own private sandbox on next use.

As built:

- `BotService.Remove` retires the removed bot's own live sandbox in the same transaction (`SandboxRepository.RetireLiveForOwner`: state `retiring`, due after the retention period) and removes the shares that name it. The provider is not called inside that transaction; the sweep pauses the sandbox within a minute.
- A member that is not the owner keeps nothing: a removed bot can make no tool call, its shares are gone, and the shared sandbox carries on for the rest untouched. Stopping its processes and rotating the token by recreating the handle are not built.
- Removing a bot with bots beneath it asks what happens to them, whatever its sandbox policy ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md#pausing-and-removing)). With `remove`, every bot of the branch is removed, and each one's own live sandbox is retired the same way. With `keep`, they move up beneath the removed bot's parent and the removed bot is no longer on their path, so a `subtree` child becomes the owner of a new sandbox on its next use, or joins the share of a `subtree` bot above, and the removed owner's sandbox is retired like any other.
- The retention period is not a space setting yet: every space keeps seven days (`SandboxSweep.Retention`). There is no screen yet to recover files from a retired sandbox or give it to another bot.

### Retiring and the sweep

`SandboxSweep` in `Drover.Engine`, run by the `SandboxSweeper` hosted service once when the server starts and then every minute, only on a server with a sandbox provider. A pass:

1. **Finds**, across spaces, through `sys.sandboxes_to_sweep(now, stuck_before, limit)` ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)), at most 50 sandboxes as ids (space, sandbox): retiring ones that are due (`retire_after` passed), retiring ones that may still be running (`expires_at`, when the provider pauses it on its own, still ahead), and rows left `creating` for more than ten minutes. A creating row's age is read from its id, a version 7 UUID, so no column holds it.
2. **Handles each** in that space's own tenant scope, as the space with no person, with no transaction open across a provider call:
   - due: `Destroy` at the provider, then delete the token from the vault and mark the row `destroyed`;
   - may still be running: `Pause`, then set `expires_at` to now so it is not paused again; one the provider no longer has is marked `destroyed`;
   - left creating: retired, due now. That frees the bot's one live row at once, so its next use makes a new sandbox; the next pass clears the old row.
3. Pausing and destroying need only the provider's control plane, so the sweep's handle carries no token, and it never reveals one. Logs carry ids and the failure kind only.

One sandbox the provider cannot destroy is logged and left retiring with `retire_after` an hour later, so it is neither retried in a hot loop nor able to fill every pass; the other sandboxes of the pass go on. One it cannot pause pauses on its own when its idle time runs out. Every step locks the row and checks its state first, so two servers sweeping at once, or the broker destroying a due sandbox of the same bot on its next use, do no harm.

A use that was already running when its sandbox was retired does not bring the row back into use: the broker leaves a retiring row retiring, and a row it finds destroyed after making a sandbox becomes retiring again, due now, so the sweep destroys what was made.

**One case can leave a sandbox at the provider.** A row records its provider id only after the provider answered. If the server stops between the provider's answer and that write, and the bot is never used again, the row holds no provider id, and the provider interface has no way to look a sandbox up by its idempotency key without making one. The sweep then marks the row destroyed, and the sandbox at the provider is left to pause on its own when its idle time runs out, and stays there paused. It needs a crash inside a window of milliseconds; if the bot is used again within E2B's idempotency memory, the retry finds the same sandbox and nothing is left behind.

Verified live on 2026-11-14 (`SandboxSweepLiveTests`, explicit): a bot ran a command in a new `base` sandbox, the bot was removed, a sweep pass left E2B reporting the sandbox paused, and a pass run as if the retention period had gone by left E2B reporting it gone, with the row destroyed and its token deleted.

## E2B adapter

`src/Drover.Adapters.E2B`. Verified facts this adapter relies on, from E2B's `spec/openapi.yml`, the envd spec and proto files, its JavaScript SDK, and live calls on 2026-11-14:

- No official .NET SDK. Official SDKs are JavaScript and Python.
- **Control plane** at `https://api.{domain}` with header `X-API-Key`. The adapter uses `POST /v2/sandboxes` (create, with an `Idempotency-Key` header), `GET /sandboxes/{id}`, `DELETE /sandboxes/{id}`, `POST /sandboxes/{id}/timeout`, `POST /sandboxes/{id}/pause`, `POST /v2/sandboxes/{id}/connect` (resume), and `PUT /sandboxes/{id}/network`. `POST /sandboxes` and `POST /sandboxes/{id}/resume` are deprecated. Errors are `{"code":<int>,"message":...}`, sometimes with `error_code` and a `Should-Retry` header.
- **No idle detection.** `timeout` is a time to live counted from the last time it was set (create, `/timeout`, or connect); with `autoPause: true` the sandbox pauses when it runs out. That is why the interface has `KeepAlive`. E2B's default is 300 seconds; Drover's `IdleTimeout` default is 15 minutes.
- **Pause and resume.** Pausing a paused sandbox answers 409; the adapter checks the state first. Connect answers 201 when it resumed and 200 when the sandbox was already running, and it only ever extends the time to live. A file written before a pause was there after resume, and the token was unchanged (live). E2B's spec does not say paused sandboxes are kept indefinitely, and a per-team maximum length exists: both are treated as unverified.
- **The in-sandbox daemon (envd)** is at `https://49983-{id}.{domain}`, the direct form, which also works for a self-hosted E2B. (The SDK uses a shared `sandbox.{domain}` host with routing headers on E2B's own domains; Drover does not.) The create answer's `domain` field was absent live, so the configured domain is used. Every call carries `X-Access-Token`; RPCs also send `Authorization: Basic base64("user:")` to run as the template's user.
- **Files** are REST: `GET /files?path=&username=` returns the bytes, `POST /files?path=&username=` with `application/octet-stream` writes them and creates parent folders.
- **Processes and listings are Connect RPC over HTTP with the JSON codec**, not gRPC. A unary call (`filesystem.Filesystem/ListDir`, `process.Process/SendSignal`) is plain JSON. `process.Process/Start` streams: request and answer are Connect frames (a flag byte, a four-byte big-endian length, JSON), ending with an end-of-stream frame that is `{}` or carries an error. Proto3 JSON leaves zero values out (a missing exit code is 0), sends int64 as a string, bytes as base64, enums as names, and times with nine fraction digits. A `Connect-Timeout-Ms` past its limit ends the stream with `deadline_exceeded` and the daemon stops the process (live).
- **Disconnecting does not stop a process**, so the adapter sends `SendSignal` with `SIGNAL_SIGKILL` whenever a command is abandoned before it exits.
- **The access token is weaker than it looks** (mechanism 2 above): `GET /sandboxes/{id}` and connect return it to any holder of the API key.
- **Network rules.** `allowOut` takes addresses, CIDR blocks, and domains including `*.example.com`; `denyOut` takes addresses and CIDR blocks only; an allowed entry beats a denied one; a domain in `allowOut` also opens DNS to 8.8.8.8. An update replaces every rule, and an omitted field is cleared. `allow_internet_access: false` is the same as denying `0.0.0.0/0`.
- **Inbound.** `allowPublicTraffic: false` only makes the sandbox's public URLs require the traffic token. The daemon's port is always reachable, guarded by the access token.
- The runtime is Apache-2.0 and can be self-hosted.

Design:

| Part | Approach |
| --- | --- |
| Control plane client | `ControlPlaneClient`, a thin class over the named client `drover-e2b-api` for the seven endpoints above. Request and response records (`Wire.cs`) are internal to the adapter. The standard resilience handler retries transient failures (every call is safe to repeat: creation carries the sandbox row's id as its idempotency key), honors `Should-Retry: false`, never repeats a policy refusal, and keeps rate limits out of the circuit breaker. |
| Daemon client | `EnvdClient` over `drover-e2b-envd`, with no resilience handler and no client timeout: a command's output streams for as long as the command runs. It allows one minute for an answer to start, gives each command its own deadline, and never repeats a started command. `ConnectEnvelope` reads and writes the frames; `ProcessEvents` reads the messages. |
| Metadata check | Before every operation but creation: `GET /sandboxes/{id}`, then refuse with `HandleMismatch` unless `drover.space` and `drover.group` match the handle. A handle whose id is not E2B's shape is refused before any request, because the id becomes part of the daemon's host name. A paused sandbox is not entered; the broker resumes it first. |
| Lifecycle | Create on first use with `autoPause: true` and `timeout` = the idle timeout. The broker calls `KeepAlive` on each use; the sandbox pauses on its own when the time runs out, and is resumed on next use. |
| Mapping | `SpaceId` and `SharingGroupId` go into metadata `drover.space` and `drover.group`. The template is `SandboxSpec.Image`, or the configured `E2BOptions.Template` (default `base`). The provider's `envdAccessToken` becomes `SandboxHandle.AccessToken`. |
| Network | Off: deny `0.0.0.0/0`. Allowlist: allow the hosts, deny `0.0.0.0/0`; entries must be domain names (optionally `*.`), because an address or CIDR block in `allowOut` would beat every deny rule. Open: deny `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `100.64.0.0/10`, `169.254.0.0/16`. On create, `allowPublicTraffic: false`. |
| Outbound | Both clients go through the server's `OutboundHttp` handler like every other client. E2B's hosts are public, so the default refusal of private addresses is right. A self-hosted E2B on a private network would need an operator-level allowance; that is not built. |
| Errors | `FailureMapping` turns statuses and Connect codes into `SandboxFailure`; a 401 from the control plane is `InvalidKey`, from the daemon `Unauthorized`. |
| Tests | `E2BConformanceTests` runs the suite offline against `tests/Drover.Adapters.Tests/Fixtures/e2b`, every one captured from the live service with ids and tokens scrubbed. `E2BWireTests` covers framing, failure mapping, network rules, and the exact requests. `E2BLiveConformanceTests` runs the same theory against the real service, explicitly: `DROVER_TEST_E2B_KEY=... DROVER_TEST_E2B_DOMAIN=e2b.dev dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Adapters.Tests.E2BLiveConformanceTests`. Each scenario creates one `base` sandbox tagged `drover.test`, with a two-minute time to live, and destroys exactly the sandboxes it created. Nothing lists and kills. |

Why own this client: the handoff says not to hand-write what a trusted library does. Here no trusted .NET library exists. The one community package is generated from the same OpenAPI document, has few users, and its filesystem coverage could not be confirmed. The surface Drover needs is small and sits entirely inside one project. If E2B ships an official .NET SDK, the adapter's internals are replaced and nothing else changes.

Risk carried: the daemon's RPC framing is implemented from the published definitions, not from an SDK. See [12-risks.md](12-risks.md).

## Docker adapter (Phase 2)

For self-hosters who want no account anywhere.

- Talks to a Docker engine through `Docker.DotNet.Enhanced` (MIT, the maintained fork).
- One container per sharing group. No shared volumes, no shared networks, no host mounts, never the Docker socket.
- Hardening by default: non-root user, all capabilities dropped, `no-new-privileges`, read-only root with a writable work volume, memory and process limits, an egress-restricted network.
- A stronger runtime (a user-space kernel or a micro virtual machine runtime) is a setting, recommended in the setup text when the operator's host has one.
- The per-sandbox token is a random value Drover injects for its own in-container agent; the metadata check uses container labels.
- The hosted service does not offer this adapter. Plain containers are not a strong enough boundary between strangers.

## Sandbox tools the bot sees

All built-in (`SandboxTools`), all granted per bot like any tool, and none on the list a new bot is granted. A sandbox tool is offered to a model only when the server has a provider, the bot's sandbox policy is not `none`, and the grant allows it through the effective-grant path and the tool gate.

| Tool | Sensitivity | Arguments | Result |
| --- | --- | --- | --- |
| `sandbox.run` | write | `command`, `cwd?`, `timeout_seconds?`, `environment?` | `exit_code`, `stdout`, `stderr`, `truncated`; `error` when the time limit stopped it |
| `sandbox.read_file` | read | `path`, `environment?` | `content` as UTF-8 text, or `binary: true` and `size` |
| `sandbox.write_file` | write | `path`, `content`, `environment?` | `path`, `bytes` |
| `sandbox.list_files` | read | `path`, `environment?` | `entries` (name, kind, size), at most 1000 |
| `sandbox.share_file` | write, idempotent | `path`, `caption?`, `environment?` | `path`, `shared` (the name it is shown under), `content_type`, `size` |
| `sandbox.enter` (use another bot's environment as its ancestor) | critical | Not built yet | |

### Sharing a file into the conversation

`sandbox.share_file` hands a file the bot made to the person. The server reads the one path the bot names, and nothing else in the sandbox (no listing, no command), through the broker like any read (`SandboxBroker.CopyOut`), and keeps it through the same file store, storage keys, kinds, and limits as a file a person attaches (`FileSharing`): `MaxFileBytes` for the file and `MaxSpaceFileMegabytes` for the space, read as the bytes arrive, and `MaxFilesPerMessage` for the reply. A refusal is the call's error result in the same sentence the upload endpoint answers with, so the bot can pass it on. The per-reply count is checked again under a lock on the reply's row, so calls of one reply that share at the same time each keep their part. A file that holds the sandbox's access token in plain bytes is refused, since a file's bytes are kept as they are and cannot be scrubbed the way printed text is. That check is best-effort: a token encoded, compressed, or split inside an archive is not caught.

The bot can name any path, another conversation's output among them, so the tool is a write: a tainted run asks before it shares. A delegated child run may not share at all, since its conversation is its own and the person does not read it; it is told to say where the file is in its answer so the bot that asked can share it.

The sandbox is not private to one conversation, so a file read from it may hold what another conversation put there. The file belongs to the conversation that asked for it: its row takes that thread's audience, `created_by_bot_id` the bot, and `message_id` the reply whose call shared it, and the server adds an `image` or `file` part, with the bot's one-line caption, to that reply and writes `message.completed` again so the reply shows it live. One call shares one file, and the file's id is the call's, so a call run again after a restart finds the file it shared instead of keeping a second. The tool is marked idempotent (`BuiltinTool.IsIdempotent`), so a call cut off by a restart runs again, and one a person approved runs again without asking again.

It is untrusted content. Reading it taints the sharing run as any read of the sandbox does (a sandbox with network, or one marked tainted), and every later run of the conversation is tainted before its agent is built, whatever the file holds, because the conversation now carries a file (`RunExecutor.TaintFromFiles`). A later run is not given the file's content: a file part in a bot's own reply is left out of what the model reads, and the call and its result already say what was shared. The person downloads it the way every file is served, so HTML or SVG a bot wrote is shown as plain text or saved, never rendered in Drover's page. Proof: `SharedFileTests` and `SharedFileApiTests`.

### Files a person attaches

A file a person attaches that the bot's model cannot read directly (anything but text and documents, and a picture for a model that does not read images) is copied into the bot's own sandbox by the server, at `attachments/{file id}/{name}` in the home folder, and the model is told the path. A PDF or office document is read out as text for the model and copied the same way when the bot may use its sandbox, so its tools can work on the original. It is copied only for a run that was given a sandbox tool, through the broker like any write, under `MaxSandboxFileBytes`, and once a run ([14-agent-framework.md](14-agent-framework.md)).

It is copied only when everyone who can reach the sandbox can see the conversation. A bot's sandbox is not private to one conversation: the space's admins can run commands in it, and the bot's runs for other people use it. So the server copies a file only when the conversation is shared with the whole space, or the space is personal and has exactly one member, where the one person who can reach the sandbox is the one who sees the conversation. Both are checked when the run reads the file rows, against the members as they are then (`FileAbilityReader.AudienceCoversSandbox`): if a personal space ever gains a second member, copies stop instead of leaking. Anywhere else the bot still reads a private conversation's text, documents, and pictures directly; a file it could open only in its sandbox is refused when it is attached, with `private_conversation` and the sentence "This conversation is private, and others in this space can reach {bot}'s sandbox, so this file cannot go there.", and refused again when the message is sent. A run whose conversation no longer qualifies gets no copy, and the bot is told why. Proof, in `AttachedFileTests`: `A_private_conversation_in_a_personal_space_with_one_member_puts_the_file_in_the_sandbox`, `A_private_conversation_in_a_shared_space_refuses_a_file_the_bot_could_open_only_in_its_sandbox`, `In_a_shared_space_an_admins_sandbox_command_sees_no_file_from_a_private_conversation_and_sees_one_from_a_shared_one`, `A_personal_space_that_gained_a_second_member_before_the_run_puts_nothing_in_the_sandbox_and_refuses_new_files` and `A_file_whose_conversation_became_private_before_the_run_is_not_copied_and_the_bot_is_told_why`.

A copy that was allowed does not stay after who may see it narrows. Three changes narrow an audience, and each retires every live sandbox of the space, tainted or not, in the same transaction, before the narrowing is visible. A personal space gains a member (`AccountService.AddMember`) or becomes a shared space (`AccountService.MakeShared`), and a shared conversation is made private to its owner (`ThreadService.MakePrivate`). The retirement holds the space's copy fence (`SandboxRepository.LockCopies`), an advisory lock. A copy takes the same fence while it reads the file's audience again and marks the sandbox (`SandboxBroker.PutFile`), and it writes only when everyone who can reach the sandbox can still see that file. A copy that already landed is on a sandbox this retirement takes out of use, and so is a private write that never marked the sandbox tainted, because the sandbox is shared by everyone who can reach it. The next use destroys the retired sandbox and makes a new one. Proof, in `AttachedFileTests`: `A_file_copied_while_a_personal_space_had_one_member_is_gone_after_a_member_joins`, `A_file_copied_while_a_space_was_personal_is_gone_after_the_space_is_shared`, `A_file_copied_from_a_shared_conversation_is_gone_after_the_conversation_is_made_private`, `A_private_write_from_before_a_member_joined_is_gone` and `A_copy_paused_while_a_member_joins_never_lands_in_the_sandbox`.

The same rule gates the bot's own writes, or the model would copy a private conversation's text into the sandbox itself. `sandbox.write_file`, `sandbox.run`, and a screen action (`computer.click`, `computer.type`, `computer.key`, `computer.scroll`) from a run whose conversation does not cover everyone who can reach the sandbox are refused before the sandbox is touched, with the sentence "This conversation is private, and others in this space can reach this bot's sandbox, so it was not changed." The audience is read in its own short tenant transaction, closed before the sandbox is touched, against the members as they are then (`SandboxBroker.RefuseWriteFromAClosedConversation`). A person's own controls have no run and are not refused here. A screenshot and a request to take the screen over do not write into the sandbox, so they are not refused here. Proof, in `SandboxBrokerTests`: `A_bot_in_a_private_conversation_of_a_shared_space_cannot_write_a_file_and_an_admins_command_sees_nothing`, `A_bot_in_a_private_conversation_of_a_shared_space_cannot_run_a_command`, `A_bot_in_a_private_conversation_of_a_personal_space_that_gained_a_member_cannot_write`, `A_bot_in_a_shared_conversation_of_a_shared_space_writes_a_file_and_runs_a_command` and `A_bot_in_a_private_conversation_of_a_personal_space_with_one_member_writes_a_file`. Proof for the screen, in `ScreenTakeoverTests`: `A_private_conversation_in_a_shared_space_cannot_type_on_the_screen`.

Before the file is written, the broker marks the sandbox row `tainted` in its own committed transaction, so a write that is cancelled or fails after the provider took it still leaves the mark (`SandboxBrokerTests.A_sandbox_is_marked_tainted_before_an_attached_file_is_written_so_a_cancel_after_the_write_leaves_the_mark`). The mark stays until the sandbox is gone: from then on every read from that sandbox (a command, a file, a listing, a screenshot) taints the run, in any conversation, even with the network `off`, because what it reads can be the file a person attached.

`sandbox.run` runs the model's command line with `bash -l -c`: the model wrote the whole command, for its own sandbox, and Drover adds no text of its own to it. Where Drover itself builds a command from model text (the computer tools below), it uses an argument list. The command, its output, and its exit code are run events, so the activity view can show a live terminal for each call.

## Computer use

A bot can see a screen in its sandbox and drive it with the mouse and keyboard, and ask a person to take the screen over when only a person should act there. This is the bare minimum: five tools to see and drive, one to ask, a fixed screen, one picture at a time.

**Turning it on.** In the bot editor's Sandbox section, a bot with a sandbox gets a **Desktop** switch (`SandboxSettings.Desktop`, in the bot's JSON settings column; no migration). Then grant the bot the computer tools on its Tools tab; no new bot is granted them. The tools are offered to a model only when all of these hold: the server has a sandbox provider, the bot's sandbox policy is not `none`, Desktop is on, the deployment allows it (`AllowDesktopSandboxes`, [16-deployment-policies.md](16-deployment-policies.md)), and something can read a screenshot: the bot's model reports `Vision` in its capability record ([15-model-providers.md](15-model-providers.md)), or a screen reader is set (below). A compatible endpoint claims no vision until the operator says so, so a model that cannot look, with nothing to look for it, is never handed a mouse. `RunExecutor.ToRunTool` is the one place that decides this.

**Saying what the bot may do.** A sandbox the bot is not granted the tools for is easy to miss, so the Sandbox section says it in words from the bot's grants as the Tools section shows them (its own and its parent's ceiling, never `tool_grants` read directly): "This bot may not use its sandbox yet." with one control, "Allow sandbox tools", and with Desktop on the same for the five screen tools ("Allow screen tools"). The control stages a grant for each tool that is off, at the most the parent allows (allow, or ask under a parent that asks), in the editor's draft; Save sets them through the same tool-grant API the Tools section uses, one by one, and the server checks each against the parent; Discard drops them. Nothing is granted by itself, and `GrantedToNewBots` is unchanged. Once allowed, the section says so quietly ("May run commands and use files", "May use the screen") with a way to the Tools section; a parent that keeps the tools off is named instead of an offer. With Desktop on, a model the catalog does not list as seeing images, and no screen reader chosen, the section says the bot cannot use the screen with this model, points at the Model section, where the screen reader is chosen, and opens it. With a reader chosen, it names the reader and offers the same way to the Model section. The collapsed summary ends with what the bot may do ("· may run commands", "· not allowed yet"), after "· screen read by <model>" when a screen reader is chosen.

**A screen reader.** A bot whose model cannot read images can still use the screen through a second model that reads it for it, and a bot whose model can may use one as an override. The Model section offers "Reads the screen for this bot", right under the model it helps, whenever it can matter: a reader is already set, the bot's model cannot read images and the space has a model that can, or the bot has a screen it cannot see. A bot whose own model sees and has no reader set is not asked. The choice is in the Model section, not the Sandbox section, because it is a choice of model, and a person looking for it looks there. Without Desktop on, the section says the reader is used once the bot has a screen and offers a way to the Sandbox section. The choice: the first option, "Its own model" (said as "Its own model (cannot read images)" when the bot's model cannot), and then every model of the space's connections that the catalog lists as reading images and available, grouped by connection. With one chosen, a hint says the bot gets a description of each screenshot in words instead of the picture, which is slower and less exact than a model that sees for itself, and that each screenshot is also a call to that model. When the bot has a screen it cannot see and no model of the space reads images, the section says so and links to connecting one. The choice is `SandboxSettings.ScreenReader`, a connection id and a model name, in the same JSON settings column (no migration: it is a setting of the bot like the others there, read only through the bot's row under row-level security, and a connection id from another space names nothing that space can read). On save, `BotService` refuses a reader whose connection is not one of this space's, whose model the catalog does not list, or whose catalog entry does not read images, each in a plain sentence. A connection removed later is not caught by a foreign key; the screenshot tool finds it gone and says so.

**The sandbox.** A sandbox with a screen is made from the provider's desktop image: `SandboxSpec.Desktop`, which the E2B adapter turns into `E2BOptions.DesktopTemplate` (default E2B's public `desktop`: Ubuntu with Xvfb, Xfce, xdotool and scrot; 8 vCPU and 8 GB, so it costs far more than `base`). `DISPLAY=:0` is in its environment. The choice is made when the sandbox is created, so it follows the sandbox's owner (in a shared sandbox, the owner's setting decides). Turning Desktop on or off takes the bot's live sandbox out of use when the bot is saved (`SandboxRepository.RetireLiveForOwner`: state `retiring`, due now); the next sandbox call makes one of the new kind, and the broker on that call, or the sweep within a minute, destroys the retired one at the provider and deletes its token. Its files go with it, and the editor says so beside the switch. Saving the bot without changing the switch keeps its sandbox.

**The display.** Before every computer action the broker runs one fixed script, written by Drover and holding no model text, that does what E2B's desktop SDK does when it creates a sandbox: if the display does not answer, remove the lock and socket a display that is gone left behind (a sandbox resumed from disk keeps them, and they stop a new display from starting), then start `Xvfb :0 -ac -screen 0 1024x768x24 -retro -dpi 96 -nolisten tcp -nolisten unix` and wait for `xdpyinfo`; if the Xfce session is not running, start `startxfce4` and wait until `xfce4-session`, `xfwm4` and `xfdesktop` all run, then three more seconds (a first screenshot was black without them, live). Both start with `setsid -f` and no open output, so they outlive the command. A session that has been up for 90 seconds is on the screen even while its window manager or desktop restarts, so the script does not wait for those or refuse because of them, and a session that came up without all three after the wait still counts. A display that will not start (exit 3, the display never answered; exit 4, no session came up; or the 90-second limit) is an error for the model; nothing else runs, and the broker logs which it was.

**The tools.** Each works in the bot's own sandbox (no `environment`). The screen is fixed at 1024 by 768.

| Tool | Sensitivity | Arguments | Runs | Result |
| --- | --- | --- | --- | --- |
| `computer.screenshot` | read | `question?` (up to 500 characters, for a screen reader) | `scrot --pointer /tmp/drover-screen-<random>.png`, read the file, `rm -f -- <file>` | `{"screenshot":"attached","width":1024,"height":768}` and the picture for the next model call; with a screen reader, `{"screenshot":"described","width":1024,"height":768,"description":"..."}` and no picture |
| `computer.click` | write | `x`, `y`, `button?` (left, right, middle), `double?` | `xdotool mousemove X Y click [--repeat 2] 1\|2\|3` | `{"done":true}` |
| `computer.type` | write | `text` (1 to 2000 characters; no control characters but new line and tab) | `xdotool type --delay 12 -- <text>` | `{"done":true}` |
| `computer.key` | write | `keys`, 1 to 5 names pressed together | `xdotool key <symbol>+<symbol>` | `{"done":true}` |
| `computer.scroll` | write | `direction` (up, down), `amount?` (1 to 20, default 3), `x?` and `y?` together | `xdotool [mousemove X Y] click --repeat N 4\|5` | `{"done":true}` |
| `computer.handover` | read; always waits for a person | `reason` (1 to 300 characters) | nothing on the sandbox | `{"done":true,"note":...}` once the person is done; `{"done":false,"note":"The person could not do it.","said":...}` when they could not (see [A person takes over](#a-person-takes-over)) |

Every action is a program and its arguments through `ISandboxProvider.Run`, never a shell line: coordinates are whole numbers checked against the screen before the sandbox is touched, a key name must be on a fixed list (letters, digits, F1 to F12, and the names E2B's SDK maps, such as `ctrl`, `enter`, `escape`, `page_down`) and becomes its X key symbol, and typed text is one argument after `--`, so nothing in it is read as an option or by a shell (`DesktopCommands`, unit-tested). An action's output is not read, so nothing from the sandbox comes back with it, and a failure is Drover's own sentence. The move and the click are one xdotool run, so the click lands where the pointer went; `mousemove --sync`, which E2B's SDK uses, is left out because it can wait for a motion that never comes when the pointer is already there.

**The picture.** A tool result is JSON, and a chat completions tool message holds only text. So a built-in tool hands its picture over with `BuiltinToolCall.Attach`, `ToolOutcome.Images` carries it to the tool gate, and the gate keeps the latest one for the run. The `TransientImages` chat-client middleware ([14-agent-framework.md](14-agent-framework.md)) adds it to the next model call alone, as a user message after the tool results: a line naming the call, and the PNG. It is never stored: not in `messages`, not in `tool_calls` (which keep the small JSON result), not in a run event, not in the session. After a restart the model sees the JSON result and takes a new screenshot. Showing screenshots in the activity view, and keeping them as files, waits for the file store.

**The description.** With a screen reader set, `computer.screenshot` first finds the reader in a short tenant transaction (the bot's setting, the connection, its catalog entry, the key, the space's effective policy) and refuses in words, before the screen is touched, when the connection is gone, the model is no longer listed as reading images, the server has no adapter for it, or its key cannot be read. Then it takes the picture as above and `ScreenDescriber` calls the reader once, with no transaction open, through `IModelClientProvider` and `IModelChatClient` like any model call, inside `OutboundAttempt.Begin` with the space's rule on private addresses: Drover's fixed instruction (`ScreenDescriber.Instruction`) as the instructions, and one user message holding the screen's size, the bot's `question` between `<question>` markers as the bot's text and not an instruction (angle brackets in it written out, so it cannot close its marker), and the PNG. No tools, no history, at most 1000 output tokens, at most `RunOptions.ScreenReadTimeout` (60 seconds), and cancelling the run cancels it. The instruction asks for what is on the screen, then each thing that can be clicked or typed into as `x,y: what it is` with x,y its centre in screen pixels, then the answer to the question, in plain text and at most 300 words. The answer is cut to 6000 characters, scrubbed of the reader connection's key, and returned as the tool result, where the gate redacts it like any result. The picture is not attached, so it never reaches the bot's model. If the reader fails, does not answer in time, or answers nothing, the tool returns that in words as an error, and nothing sends the picture to another model. A bot whose model cannot read images and that has no reader is refused the same way if it ever reaches the tool.

**What a screenshot can show.** Anything on that screen: a page the bot opened, a password field it typed into, a token in a terminal. Drover's redaction works on text and cannot reach into pixels, so a secret on the screen goes to the model provider as it is. Keep secrets out of a sandbox with a screen; with a screen reader the picture goes to the reader's provider instead, as it is. A screenshot is read from the sandbox like a file, so when the sandbox's network is not `off` it taints the run, and later consequential tools ask first ([06-mcp.md](06-mcp.md)); an action, which reads nothing back, does not. A description is made from the same pixels, so it taints the run exactly as the picture does, and it reaches the bot only as a tool result, never as instructions: what the screen says is text a sandbox drew.

Verified live on 2026-11-14 (`ComputerLiveTests`, explicit): a run with a scripted model took a 1024 by 768 screenshot that arrived as the image of the next model request, clicked the terminal launcher, scrolled, typed `echo drover-$((6*7))`, pressed Enter, and a second screenshot showed the terminal with `drover-42` printed.

Verified live on 2026-11-14 (`ScreenReaderLiveTests`, explicit): a bot with a text-only scripted model and a scripted screen reader took a screenshot of a real desktop sandbox; the reader's one request carried a valid 1024 by 768 PNG and the fixed instruction, the bot got the description as the tool result, and none of the bot model's requests held an image. No model that reads images was at hand, so the reader's answer was scripted: what a real one writes was not checked.

Not built: a streamed view of the screen (a person's sandbox screen shows a fresh picture every two seconds, below), screenshots in the activity view, dragging, holding a key down, a screen size other than 1024 by 768, and a smaller Drover-owned desktop image.

### A person takes over

Some things on a screen only a person should do: a sign-in, a passkey, a check that a human is there. `computer.handover` lets a bot say so and wait, instead of giving up or faking a result. It is offered like the other computer tools (granted on the Tools tab, only with a screen), and it always waits for a person (`AlwaysAsks`), whatever its grant says.

**The wait is an approval.** The call goes through the same gate and the same machinery as an approval ([03-run-lifecycle.md](03-run-lifecycle.md#approval)): the row is `pending_approval`, the log has `tool.approval_requested`, the session is saved, and the run (and the tree above it) is `waiting`. The one difference is the reason: a run whose step asked for a person at the screen waits with `awaiting_takeover`, not `awaiting_approval`. A restart keeps the wait, and the answer is handed over once, as for any approval.

**The screen is held.** While any run of a bot that shares the sandbox (the same sharing-group owner, [Sharing policies](#sharing-policies)) waits with `awaiting_takeover`, every bot's use of that sandbox is refused before the sandbox is touched (`SandboxBroker.LocateIn`, for any call made by a tool: the computer tools, and `sandbox.run` and the file tools too, since a command could read the screen or the keyboard as well as a computer tool could), with a result that says a person has the screen and that an owner or admin can see who, and stop that run, on the bot's sandbox page. The hold is read from `runs`, which every member's runs can read, so it holds against another member's conversation that cannot see the call. A person's own input from the screen is not held. The hold stops bots from starting anything new in the sandbox; it does not stop a process a bot started there before, so the card says exactly that: Drover records nothing the person types or clicks, and no bot can start anything new on the screen while they hold it. The hold ends when the answer is stored (the run's reason goes back to `awaiting_approval` until a worker takes it up), or when the run ends. A handover call the bot's rules will refuse (block or stop) holds nothing.

**Seeing and ending a hold.** `BotSandboxStatus.Hold` (`SandboxHold`: the root run's id, whether it is this person's, and `runs.waiting_since`) is set while a run of a bot that shares the sandbox waits with `awaiting_takeover`. The sandbox screen says "A bot asked you (or another member of this space) to take over this screen on ..." (to the person it asked, also "Do it on the screen below, then answer in the conversation."), and offers "Stop the run that holds it", which asks first and then asks for that run to be cancelled through the ordinary cancel route, which an owner or admin may use on any run. Cancelling is a request, so the page says "Stopping the run. The screen frees once it stops." in place of the button until the state it reads every ten seconds no longer shows the hold. No conversation content and no other person's name is shown: a person's row is readable only by that person, and naming them would need a `sys` function. A hold nobody answers lasts until it is answered or stopped; a time limit is not built.

**Answering.** Two routes, beside the approval's decision route, under `/api/spaces/{spaceId}/tool-calls/{id}`:

| Route | Does | Who |
| --- | --- | --- |
| `POST /takeover` | Records that the person took over: one `sandbox.taken_over` audit row. The call does not change | An owner or admin who can see the call (the people who use a bot's screen) |
| `POST /handback` | `ToolHandbackRequest`: `done`, and an optional `note`. Done approves the call, which runs and returns that the person is done; could not stores `{"done":false,...,"said":note}` as the call's result. One `sandbox.handed_back` audit row with the outcome | Like an approval: the run's person, or a space admin, who can see the call |

Both are refused with 409 for a call that is not a request for a person, and the decision route refuses a request for a person the same way. A second answer gets 409. `ApprovalService` holds all three, with one path for answering. Nothing the person typed, clicked, or saw on the screen is part of the answer, a run event, a tool result, or an audit row: the person's input reaches the sandbox through the screen's own route as before, which records only its kind. The bot learns what happened only from the answer, and from the screen when it next takes a screenshot, which taints like any other.

**In the conversation.** The call shows as its own card where an approval card would be (`TakeoverCard`, `Drover.Ui/Chat`): "<Bot> needs you at its screen", the reason, "Open the screen", and "I cannot do it" with an optional line for the bot ("Tell <Bot> I could not"). "Open the screen" records the takeover and opens the screen rail beside the conversation with Take control on (on a phone the rail takes the conversation's place, with its close control to go back). The person holds the screen only for a request they took over this way; opening the rail by its link answers nothing. While they hold it the rail shows the card with "I'm done" and "I could not do it" above the picture, the focus moves to "I'm done", and the card in the conversation says to use I'm done at the screen, so there is one set of controls. Once handed back the rail's card goes, Take control turns off, and the focus moves to the rail's close control. Until the page knows whether the person can use the screen the card offers no answer. A member is told that owners and admins use the bot's screen and answers with "It is done" or "I cannot do it", neither pressed on them; for a bot with no screen (no provider, no sandbox, or Desktop off) the card says so with a link to what it needs (the status page without a provider, the bot's settings otherwise) and offers only "I cannot do it"; a sandbox that is stopped or not made yet still has a screen, which offers to start it. An answered card says "You told <Bot> it is done" or "You told <Bot> you could not do it"; one the run's end closed without an answer is the plain tool card. A request from a bot beneath, shown inside the work handed to it, names that bot: its screen is its own, so the card points to its sandbox page and offers "It is done" and "I cannot do it" (a member still gets the member wording). The bot editor's Sandbox section says, beside the screen tools, that asking a person to take over is a tool of its own, allowed in the Tools section; it is not part of "Allow screen tools", so the screen tools a bot already has stay allowed. The screen's address names the request the person took over (`/talk/{botId}/screen?held={toolCallId}`), so after a reload they still hold it while it waits. No thinking state shows while the run waits; the composer and the crew treat it as a wait on the person.

Not built: a vault that fills passwords for the bot, and recording what a person does to teach the bot.

## People and the sandbox

A person can see a bot's sandbox, drive it, and power it, on a screen of its own: `/bots/{botId}/sandbox`. Its job: show a person what a bot's sandbox is doing right now and let them take over or restart it. The bot editor's Sandbox section shows the state in one line ("Paused · used 10 minutes ago") with a link to it; the settings stay in the editor.

**Who.** The people who may change the bot: the space's owners and admins, the same check the editor's save uses. Everything is the bot's own sandbox, the one its sharing group owns (for a `subtree` bot, the highest `subtree` ancestor's), never another bot's by name.

**The service.** `SandboxControlService` in `Drover.Engine` checks the person (member of the space, owner or admin, the bot there and not removed) in one short tenant transaction, then calls `SandboxBroker` with no transaction open, then writes the audit row in another short one. The broker is the only caller of the provider for people as for bots (`SandboxCaller`: a bot's tool call, or a person with no call), so every limit that binds the bot binds the person: `AllowDesktopSandboxes`, `AllowOpenSandboxNetwork`, `MaxSandboxCommandSeconds`, `MaxSandboxFileBytes`, `MaxSandboxIdleMinutes`, the network rules, and whether the server has a provider at all. A person's use is not a run, so it taints nothing and names no tool call. The service is registered on every server; with no provider it reports that and refuses each action in words.

| Route under `/api/spaces/{spaceId}/bots/{botId}/sandbox` | Does | Answer |
| --- | --- | --- |
| `GET /` | The state, from Drover's rows alone (no provider call) | `BotSandboxStatus`: whether the server has a provider, whether the bot's policy gives it a sandbox, the state (`none`, `starting`, `running`, `paused`, `replacing`), the last use, whether it has a screen, whether a run of this bot is working now, and whether this server offers screens at all (`DesktopOffered`, from `AllowDesktopSandboxes`), so a sandbox without one can say why. `StartFailed` is set with `none` when the bot's newest row was destroyed without ever holding a provider id: a creation that failed and the sweep cleared. The screen then says the last start did not finish, in place of "No sandbox yet" |
| `POST /pause` | Pauses it at the provider; files are kept; the row is `paused` | The state |
| `POST /resume` | Resumes a paused one, or keeps a running one; makes none | The state |
| `POST /rebuild` | Takes the live sandbox out of use (`retiring`, due now), destroys it at the provider, deletes its token, and makes a fresh one at once, as a change of the Desktop switch does on next use | The state |
| `POST /turn-off` | Destroys it at the provider now and marks the row `destroyed`; its token is deleted; the next use makes a new one | The state |
| `POST /screen` | The screen as `image/png`, `Cache-Control: no-store`; starts the display as the bot's tools do; resumes a paused sandbox and makes a missing one unless `wake=false` | The picture |
| `POST /input` | `SandboxInputRequest`: `click` (x, y, button, twice), `type` (text), `key` (keys), or `scroll` (direction, amount, x, y) | 204 |
| `POST /commands` | `SandboxCommandRequest`: one bash command line, made on first use | `SandboxCommandResponse`: exit status, stdout, stderr (each cut like a bot's), `truncated`, `timed_out`, `new_sandbox` |

The screen is a POST, with the antiforgery token like every state-changing call, because an explicit capture can resume or make a sandbox; it is under `/api`, which the service worker never caches, and nothing stores the picture. Automatic refreshes send `wake=false`. They look only at a running sandbox, leave its idle timer and state alone, and cannot undo a pause. A refusal is a problem document whose title is the broker's own sentence: 400 for a wrong input or command, 409 for a refusal by a rule, the bot's settings, or the provider, 403 for a member who may not change the bot, 404 for a person outside the space or a bot that is not there. No answer carries the provider's id, the token, or which provider it is.

**The same commands as a bot's.** A click, typed text, a key, and a scroll are built by `DesktopCommands`, the code behind the computer tools: the same argument lists, the same bounds (a point on the 1024 by 768 screen, 1 to 2000 characters of text with no control characters but new line and tab, 1 to 5 keys from the fixed list, 1 to 20 wheel clicks), checked before the sandbox is touched. An input does not make a sandbox; it resumes a paused one. A person's command runs with `bash -l -c` like `sandbox.run`, at most 4000 characters, for at most five minutes and never past `MaxSandboxCommandSeconds` (the request waits for it), and its output is scrubbed of the access token.

**Screens that meet.** Requests for the same bot's screen and wake setting that arrive while one is being taken share it, so a page left open twice does not double the provider calls.

**State without asking the provider.** The provider pauses a sandbox on its own once its idle time runs out, so a `running` row past its `expires_at` reads as `paused`. `replacing` means a sandbox taken out of use is not destroyed yet (the provider could not be reached, or the Desktop switch changed and the next use has not come).

**Audit.** Each action writes one `audit_log` row after it is done: `sandbox.paused`, `sandbox.resumed`, `sandbox.rebuilt`, `sandbox.turned_off`, `sandbox.command`, or `sandbox.input` with the kind of input. Taking over the screen at a bot's request and handing it back write `sandbox.taken_over` and `sandbox.handed_back` with the outcome ([A person takes over](#a-person-takes-over)). Never the text typed, the command, its output, or a picture. Looking at the screen and reading the state write nothing. A power control that found no sandbox to act on writes nothing.

**The screen in the interface.** A header with the bot's name, the state in words (never by color alone), whether the bot is working, the last use, and the power controls: Pause or Resume, Rebuild, Turn off. Rebuild and Turn off ask first and say what is lost; cancelling sends nothing. Two tabs: Screen (only for a sandbox with one) and Terminal. The address follows the selected tab, including when the interactive terminal opens on Screen. The picture fills the width it has and keeps its shape; it is fetched every two seconds while the Screen tab is shown, the sandbox runs, and the page is visible, and waits twice as long after each failure in a row, up to thirty seconds. It is never fetched for a paused sandbox, since that would resume it. Pausing removes the picture and clears desktop input. Once a new picture has arrived after resuming, the screen starts in view-only mode unless the person holds it. "Send desktop input" is off by default; on, a tap on the picture clicks at the same place of the sandbox's own screen (mapped from the size the picture is shown at, `ScreenPoint`), a small control picks click, right click, or double click, and there are a text field with Type text, key buttons (Enter, Tab, Escape, Backspace, the arrows), key combinations in a menu (Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z, Ctrl+A, Ctrl+L, Alt+Tab, Alt+F4), and scroll buttons. By keyboard, the arrow keys move a pointer over the picture and Enter clicks there. While control is on and a run of the bot is working, the screen says the bot has an active run and may use the same desktop.

**On a phone.** The picture can be enlarged up to four times and moved, with or without control: pinch and drag on touch, or Zoom out, Zoom in, and Fit (with the zoom in words, "200%"); enlarged and view only, the picture takes the focus and the arrow keys move the view; in control, the view follows the keyboard pointer. At fit a finger on the picture scrolls the page; enlarged it moves the picture. A tap still clicks the sandbox pixel under the finger at any zoom and offset (`ScreenZoom` holds the view as shares of the frame, `ScreenPoint` maps the tap); a drag or pinch (more than 10 px of travel) never clicks. A gesture is drawn by the page's script and then handed to the page, which owns the view. After any input a person sends (tap, text, key, scroll) the picture is fetched again 300 ms later, then the two-second timer carries on; pictures are fetched by one loop, so quick inputs leave at most one in flight and one due. The text field's keyboard key reads Send (`enterkeyhint`), Enter sends, and "Press Enter after typing" (off by default, kept for the visit) adds Enter to the text and changes the button to "Type and press Enter"; the field then clears and keeps the focus, and Send does not take it. On touch devices, while the field has focus, the picture shrinks to make room for the form when both fit above the keyboard. In a shorter viewport the picture keeps its size and the form scrolls into view. The password switch sits before the field.

The Terminal tab lists this visit's shell commands with their exit code and output above a command field. For a sandbox with a desktop, Open interactive terminal launches the sandbox's native terminal and switches to Screen. The native terminal keeps its shell state and supports interactive prompts.

**Entering passwords.** A person focuses the remote password prompt and selects Type a password on Screen. Drover masks the local field, clears it when submitted, and sends the value as UTF-8 over the provider process's stdin. It goes to the focused window without an Enter key. The value stays out of the command line, chat, tool results, and the audit entry. This still uses the shared sandbox and its focused window, so the bot can see the desktop and use the resulting signed-in session. A bot that meets a sign-in asks for the person with `computer.handover` (its `computer.type` description says so) and goes on once they hand the screen back ([A person takes over](#a-person-takes-over)); while it waits, no bot that shares the sandbox can start anything new in it. A process a bot started there before the hold keeps running, so the hold is not a promise that nothing on the machine watches; Drover itself records nothing the person types or clicks. Direct delivery of reusable secrets to a bot requires a separate, scoped credential flow.

**Beside the conversation.** The screen is one component, `SandboxScreen` (`Drover.Ui/Bots`): the picture, its refresh, zoom, and desktop input, all as described above. The sandbox screen's Screen tab shows it, and so does the conversation. Its job there: let a person watch what the bot does on its screen while they talk to it, and send desktop input when they choose. In the main conversation's header, next to Summary and Side chats, a **Screen** link (`/talk/{botId}/screen`) opens it in the rail beside the conversation (`ScreenPanel`), which on a wide window shares the width evenly with the conversation so the picture can be read. The link is offered only for a bot whose sandbox has a screen that the person may watch: the page reads `GET /sandbox` once for the shown bot and again each time the bot stops working, and a refusal (403 for a member who is not an owner or admin) or a failure leaves the link out. Above the picture the panel says the state in words and whether the bot is working, with a link "Open terminal and power controls" to the sandbox screen with Terminal selected. The panel reads the state when it opens and every ten seconds while it is open. Opened for a bot with no sandbox, it says so and links to the bot's settings; for a sandbox without a screen, it says whether the bot's Desktop setting is off (with a link to the settings) or the server offers no screens (with a link to the terminal, or to the settings for a bot with no sandbox at all, since the server's rule is the reason either way); a first read that fails says so with Try again, and the next read that works replaces it without a tap; for a person who may not, it says owners and admins watch and use the screens, and the conversation works as before. The sandbox screen itself reads the state every ten seconds while open, on whichever tab, and a sandbox without a screen says which of the two it is above its tabs.

**Every state without a picture says why, and offers the tap that brings one.** Looking never makes or resumes a sandbox, but the screen has one button that does: "Start the sandbox" when there is none yet, "Resume the sandbox" when it is paused. The tap asks for the picture, which makes or resumes the sandbox on the server; the screen says "Starting the sandbox" or "Resuming the sandbox" while it waits, and whatever shows the screen reads the state again as soon as the picture has come or failed. A start that fails says why in the server's sentence and is not tried again on its own, since each try asks the provider for a sandbox; the button stays, as "Try again" when the failed start left the sandbox starting. No sandbox yet ("It starts when the bot first uses it"), starting ("The screen shows once it runs"), paused, being replaced, no screen (Desktop off, or none offered by the server), waiting for the first picture ("Getting the screen"), and a picture that failed (the server's sentence, or that it did not answer in time, then "The screen is tried again shortly"). No failure ends the ten-second state reads or the picture's own refresh: a request the client's own time limit ends, or an answer that is not what was asked for (a page a proxy put in front of the server, a picture that is not a PNG), is reported like one the server refused (`Failures`), anything else is logged, and the loop carries on; only closing the screen ends it, and closing never fails because of how the refresh ended.

No deployment rule was added for this: a person can do nothing in the sandbox that the bot they configure cannot, and every rule that bounds the bot's sandbox bounds theirs.

Verified live on 2026-11-14 (`SandboxControlLiveTests`, explicit): a person's command made a desktop sandbox and printed `drover-person-42`; pause left it paused at the provider and resume running; the screen was a 1024 by 768 picture; a click on the terminal launcher, typed text, and Enter opened a terminal that printed `person-42`, and the second picture differed; rebuild left the old sandbox gone at the provider and the next command ran on a new one; turn off left that one gone.
