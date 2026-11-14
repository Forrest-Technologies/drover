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

Bots never hold a sandbox. `SandboxBroker` in `Drover.Engine` (`src/Drover.Engine/Sandboxes/`) is the only caller of `ISandboxProvider` for a bot's work; the only other caller is the sweep ([Retiring and the sweep](#retiring-and-the-sweep)), which only pauses and destroys. Both are registered, with the four sandbox tools, only on a server whose operator configured a provider key (below); a server without one has no sandbox tools in its catalog at all.

For each sandbox tool call it:

1. **Finds the sandbox, in one short tenant transaction** as the run's person: loads the calling bot, refuses if its sandbox policy is `none`, resolves its sharing group from its path to the root (`SandboxGroups.OwnerOf`, a pure function in `Drover.Domain`), and, when the call names another bot in `environment`, checks the caller may work there (`SandboxGroups.MayEnter`). It loads the owner's live row (`creating`, `running` or `paused`), or inserts a `creating` one, and reveals the access token from the vault. The transaction closes before any provider call. A partial unique index (`ux_sandboxes_one_live_per_owner`, migration `SandboxPerGroup`) allows one live row per owner, so two first uses at once make one row: the loser's insert fails, and it reads the winner's row.
2. **Makes it ready, with no transaction open.** A new row: `Create` with the row's id as the idempotency key, then a second short transaction stores the token with `ISecretVault.Store(SecretKind.SandboxToken, ...)` as a space secret (every member's runs use the bot's sandbox), sets the provider's id, and marks the row `running`. A stored row: `Resume` with the idle time. Resuming a running sandbox only keeps it longer, and the provider pauses a sandbox on its own, so the row cannot say which it is; one call covers both. A new token from `Resume` replaces the old one in the vault. A sandbox the provider no longer has is marked `destroyed`, its token deleted, and a new one made once; the tool's result then says its earlier files are gone.
3. **Applies the network rules** when they differ from the last ones it set on that sandbox (kept in memory, so after a restart they are set once again). A change to a bot's network setting reaches its running sandbox on the next use.
4. **Runs the operation.** A command streams its output into the run as `tool.output` events ([03-run-lifecycle.md](03-run-lifecycle.md)) and returns exit code, stdout and stderr, each cut to half the run's tool result limit. A command's own limit keeps the sandbox up for at least that long plus a minute, and afterwards the sandbox goes back to its idle time.
5. **Records the use** in a last short transaction: the row's `last_used_at`, `expires_at` and state, and the tool call's `sandbox_id`. When the sandbox's network is not `off`, reading its output (a command, a file, a listing) taints the run, so later consequential tools ask first ([06-mcp.md](06-mcp.md)).

The access token goes from the vault into the handle and nowhere else. Output is scrubbed of it before any event or result, including a token split across two pieces of output (the tail that could start one is held back until more arrives); the run's own redactor applies on top. It is never logged, put in an exception, a tool argument, or a run event. A provider failure reaches the model as Drover's own sentence, never the provider's text.

Limits come from the deployment policy and only narrow ([16-deployment-policies.md](16-deployment-policies.md)): a command runs at most `MaxSandboxCommandSeconds` whatever the bot asks; a file read or written is at most `MaxSandboxFileBytes`; the idle time is the bot's setting, or 15 minutes, never above `MaxSandboxIdleMinutes`; and a bot set to open network is refused where `AllowOpenSandboxNetwork` is off. `BotService` refuses to save an idle time or a network setting past them.

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
- Removing a bot does not remove its subtree. Its descendants stay; the removed bot no longer counts on their path, so a `subtree` child becomes the owner of a new sandbox on its next use, and the removed owner's sandbox is retired like any other.
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
| `sandbox.enter` (use another bot's environment as its ancestor) | critical | Not built yet | |

`sandbox.run` runs the model's command line with `bash -l -c`: the model wrote the whole command, for its own sandbox, and Drover adds no text of its own to it. Where Drover itself builds a command from model text (the computer tools below), it uses an argument list. The command, its output, and its exit code are run events, so the activity view can show a live terminal for each call.

## Computer use

A bot can see a screen in its sandbox and drive it with the mouse and keyboard. This is the bare minimum: five tools, a fixed screen, one picture at a time.

**Turning it on.** In the bot editor's Sandbox section, a bot with a sandbox gets a **Desktop** switch (`SandboxSettings.Desktop`, in the bot's JSON settings column; no migration). Then grant the bot the computer tools on its Tools tab; no new bot is granted them. The tools are offered to a model only when all of these hold: the server has a sandbox provider, the bot's sandbox policy is not `none`, Desktop is on, the deployment allows it (`AllowDesktopSandboxes`, [16-deployment-policies.md](16-deployment-policies.md)), and the bot's model reports `Vision` in its capability record ([15-model-providers.md](15-model-providers.md)). A compatible endpoint claims no vision until the operator says so, so a model that cannot look is never handed a mouse.

**Saying what the bot may do.** A sandbox the bot is not granted the tools for is easy to miss, so the Sandbox section says it in words from the bot's grants as the Tools section shows them (its own and its parent's ceiling, never `tool_grants` read directly): "This bot may not use its sandbox yet." with one control, "Allow sandbox tools", and with Desktop on the same for the five screen tools ("Allow screen tools"). The control stages a grant for each tool that is off, at the most the parent allows (allow, or ask under a parent that asks), in the editor's draft; Save sets them through the same tool-grant API the Tools section uses, one by one, and the server checks each against the parent; Discard drops them. Nothing is granted by itself, and `GrantedToNewBots` is unchanged. Once allowed, the section says so quietly ("May run commands and use files", "May use the screen") with a way to the Tools section; a parent that keeps the tools off is named instead of an offer. With Desktop on and a model the catalog does not list as seeing images, the section says the bot cannot use the screen with this model and opens the Model section. The collapsed summary ends with what the bot may do ("· may run commands", "· not allowed yet").

**The sandbox.** A sandbox with a screen is made from the provider's desktop image: `SandboxSpec.Desktop`, which the E2B adapter turns into `E2BOptions.DesktopTemplate` (default E2B's public `desktop`: Ubuntu with Xvfb, Xfce, xdotool and scrot; 8 vCPU and 8 GB, so it costs far more than `base`). `DISPLAY=:0` is in its environment. The choice is made when the sandbox is created, so it follows the sandbox's owner (in a shared sandbox, the owner's setting decides). Turning Desktop on or off takes the bot's live sandbox out of use when the bot is saved (`SandboxRepository.RetireLiveForOwner`: state `retiring`, due now); the next sandbox call makes one of the new kind, and the broker on that call, or the sweep within a minute, destroys the retired one at the provider and deletes its token. Its files go with it, and the editor says so beside the switch. Saving the bot without changing the switch keeps its sandbox.

**The display.** Before every computer action the broker runs one fixed script, written by Drover and holding no model text, that does what E2B's desktop SDK does when it creates a sandbox: if the display does not answer, start `Xvfb :0 -ac -screen 0 1024x768x24 -retro -dpi 96 -nolisten tcp -nolisten unix` and wait for `xdpyinfo`; if the Xfce session is not running, start `startxfce4` and wait until `xfce4-session`, `xfwm4` and `xfdesktop` all run, then three more seconds (a first screenshot was black without them, live). Both start with `setsid -f` and no open output, so they outlive the command. A display that will not start is an error for the model; nothing else runs.

**The tools.** Each works in the bot's own sandbox (no `environment`). The screen is fixed at 1024 by 768.

| Tool | Sensitivity | Arguments | Runs | Result |
| --- | --- | --- | --- | --- |
| `computer.screenshot` | read | none | `scrot --pointer /tmp/drover-screen-<random>.png`, read the file, `rm -f -- <file>` | `{"screenshot":"attached","width":1024,"height":768}`, and the picture for the next model call |
| `computer.click` | write | `x`, `y`, `button?` (left, right, middle), `double?` | `xdotool mousemove X Y click [--repeat 2] 1\|2\|3` | `{"done":true}` |
| `computer.type` | write | `text` (1 to 2000 characters; no control characters but new line and tab) | `xdotool type --delay 12 -- <text>` | `{"done":true}` |
| `computer.key` | write | `keys`, 1 to 5 names pressed together | `xdotool key <symbol>+<symbol>` | `{"done":true}` |
| `computer.scroll` | write | `direction` (up, down), `amount?` (1 to 20, default 3), `x?` and `y?` together | `xdotool [mousemove X Y] click --repeat N 4\|5` | `{"done":true}` |

Every action is a program and its arguments through `ISandboxProvider.Run`, never a shell line: coordinates are whole numbers checked against the screen before the sandbox is touched, a key name must be on a fixed list (letters, digits, F1 to F12, and the names E2B's SDK maps, such as `ctrl`, `enter`, `escape`, `page_down`) and becomes its X key symbol, and typed text is one argument after `--`, so nothing in it is read as an option or by a shell (`DesktopCommands`, unit-tested). An action's output is not read, so nothing from the sandbox comes back with it, and a failure is Drover's own sentence. The move and the click are one xdotool run, so the click lands where the pointer went; `mousemove --sync`, which E2B's SDK uses, is left out because it can wait for a motion that never comes when the pointer is already there.

**The picture.** A tool result is JSON, and a chat completions tool message holds only text. So a built-in tool hands its picture over with `BuiltinToolCall.Attach`, `ToolOutcome.Images` carries it to the tool gate, and the gate keeps the latest one for the run. The `TransientImages` chat-client middleware ([14-agent-framework.md](14-agent-framework.md)) adds it to the next model call alone, as a user message after the tool results: a line naming the call, and the PNG. It is never stored: not in `messages`, not in `tool_calls` (which keep the small JSON result), not in a run event, not in the session. After a restart the model sees the JSON result and takes a new screenshot. Showing screenshots in the activity view, and keeping them as files, waits for the file store.

**What a screenshot can show.** Anything on that screen: a page the bot opened, a password field it typed into, a token in a terminal. Drover's redaction works on text and cannot reach into pixels, so a secret on the screen goes to the model provider as it is. Keep secrets out of a sandbox with a screen. A screenshot is read from the sandbox like a file, so when the sandbox's network is not `off` it taints the run, and later consequential tools ask first ([06-mcp.md](06-mcp.md)); an action, which reads nothing back, does not.

Verified live on 2026-11-14 (`ComputerLiveTests`, explicit): a run with a scripted model took a 1024 by 768 screenshot that arrived as the image of the next model request, clicked the terminal launcher, scrolled, typed `echo drover-$((6*7))`, pressed Enter, and a second screenshot showed the terminal with `drover-42` printed.

Not built: a streamed view of the screen (a person's sandbox screen shows a fresh picture every two seconds, below), screenshots in the activity view, dragging, holding a key down, a screen size other than 1024 by 768, and a smaller Drover-owned desktop image.

## People and the sandbox

A person can see a bot's sandbox, drive it, and power it, on a screen of its own: `/bots/{botId}/sandbox`. Its job: show a person what a bot's sandbox is doing right now and let them take over or restart it. The bot editor's Sandbox section shows the state in one line ("Paused · used 10 minutes ago") with a link to it; the settings stay in the editor.

**Who.** The people who may change the bot: the space's owners and admins, the same check the editor's save uses. Everything is the bot's own sandbox, the one its sharing group owns (for a `subtree` bot, the highest `subtree` ancestor's), never another bot's by name.

**The service.** `SandboxControlService` in `Drover.Engine` checks the person (member of the space, owner or admin, the bot there and not removed) in one short tenant transaction, then calls `SandboxBroker` with no transaction open, then writes the audit row in another short one. The broker is the only caller of the provider for people as for bots (`SandboxCaller`: a bot's tool call, or a person with no call), so every limit that binds the bot binds the person: `AllowDesktopSandboxes`, `AllowOpenSandboxNetwork`, `MaxSandboxCommandSeconds`, `MaxSandboxFileBytes`, `MaxSandboxIdleMinutes`, the network rules, and whether the server has a provider at all. A person's use is not a run, so it taints nothing and names no tool call. The service is registered on every server; with no provider it reports that and refuses each action in words.

| Route under `/api/spaces/{spaceId}/bots/{botId}/sandbox` | Does | Answer |
| --- | --- | --- |
| `GET /` | The state, from Drover's rows alone (no provider call) | `BotSandboxStatus`: whether the server has a provider, whether the bot's policy gives it a sandbox, the state (`none`, `starting`, `running`, `paused`, `replacing`), the last use, whether it has a screen, whether a run of this bot is working now |
| `POST /pause` | Pauses it at the provider; files are kept; the row is `paused` | The state |
| `POST /resume` | Resumes a paused one, or keeps a running one; makes none | The state |
| `POST /rebuild` | Takes the live sandbox out of use (`retiring`, due now), destroys it at the provider, deletes its token, and makes a fresh one at once, as a change of the Desktop switch does on next use | The state |
| `POST /turn-off` | Destroys it at the provider now and marks the row `destroyed`; its token is deleted; the next use makes a new one | The state |
| `POST /screen` | The screen as `image/png`, `Cache-Control: no-store`; starts the display as the bot's tools do; resumes a paused sandbox and makes a missing one, as a bot's first use does | The picture |
| `POST /input` | `SandboxInputRequest`: `click` (x, y, button, twice), `type` (text), `key` (keys), or `scroll` (direction, amount, x, y) | 204 |
| `POST /commands` | `SandboxCommandRequest`: one bash command line, made on first use | `SandboxCommandResponse`: exit status, stdout, stderr (each cut like a bot's), `truncated`, `timed_out`, `new_sandbox` |

The screen is a POST, with the antiforgery token like every state-changing call, because looking can resume or make a sandbox; it is under `/api`, which the service worker never caches, and nothing stores the picture. A refusal is a problem document whose title is the broker's own sentence: 400 for a wrong input or command, 409 for a refusal by a rule, the bot's settings, or the provider, 403 for a member who may not change the bot, 404 for a person outside the space or a bot that is not there. No answer carries the provider's id, the token, or which provider it is.

**The same commands as a bot's.** A click, typed text, a key, and a scroll are built by `DesktopCommands`, the code behind the computer tools: the same argument lists, the same bounds (a point on the 1024 by 768 screen, 1 to 2000 characters of text with no control characters but new line and tab, 1 to 5 keys from the fixed list, 1 to 20 wheel clicks), checked before the sandbox is touched. An input does not make a sandbox; it resumes a paused one. A person's command runs with `bash -l -c` like `sandbox.run`, at most 4000 characters, for at most five minutes and never past `MaxSandboxCommandSeconds` (the request waits for it), and its output is scrubbed of the access token.

**Screens that meet.** Requests for the same bot's screen that arrive while one is being taken share it, so a page left open twice does not double the provider calls.

**State without asking the provider.** The provider pauses a sandbox on its own once its idle time runs out, so a `running` row past its `expires_at` reads as `paused`. `replacing` means a sandbox taken out of use is not destroyed yet (the provider could not be reached, or the Desktop switch changed and the next use has not come).

**Audit.** Each action writes one `audit_log` row after it is done: `sandbox.paused`, `sandbox.resumed`, `sandbox.rebuilt`, `sandbox.turned_off`, `sandbox.command`, or `sandbox.input` with the kind of input. Never the text typed, the command, its output, or a picture. Looking at the screen and reading the state write nothing. A power control that found no sandbox to act on writes nothing.

**The screen in the interface.** A header with the bot's name, the state in words (never by color alone), whether the bot is working, the last use, and the power controls: Pause or Resume, Rebuild, Turn off. Rebuild and Turn off ask first and say what is lost; cancelling sends nothing. Two tabs: Screen (only for a sandbox with one) and Terminal. The picture fills the width it has and keeps its shape; it is fetched every two seconds while the Screen tab is shown, the sandbox runs, and the page is visible, and waits twice as long after each failure in a row, up to thirty seconds. It is never fetched for a paused sandbox, since that would resume it. "Take control" is off by default; on, a tap on the picture clicks at the same place of the sandbox's own screen (mapped from the size the picture is shown at, `ScreenPoint`), a small control picks click, right click, or double click, and there are a text field with Send, key buttons (Enter, Tab, Escape, Backspace, the arrows), key combinations in a menu (Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z, Ctrl+A, Ctrl+L, Alt+Tab, Alt+F4), and scroll buttons. By keyboard, the arrow keys move a pointer over the picture and Enter clicks there. While control is on and a run of the bot is working, the screen says the bot is working in this sandbox right now. The Terminal tab lists this visit's commands with their exit code and output above a command field.

No deployment rule was added for this: a person can do nothing in the sandbox that the bot they configure cannot, and every rule that bounds the bot's sandbox bounds theirs.

Verified live on 2026-11-14 (`SandboxControlLiveTests`, explicit): a person's command made a desktop sandbox and printed `drover-person-42`; pause left it paused at the provider and resume running; the screen was a 1024 by 768 picture; a click on the terminal launcher, typed text, and Enter opened a terminal that printed `person-42`, and the second picture differed; rebuild left the old sandbox gone at the provider and the next command ran on a new one; turn off left that one gone.
