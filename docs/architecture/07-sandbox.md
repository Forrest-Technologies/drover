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

Bots never hold a sandbox. `SandboxBroker` in `Drover.Engine` is the only caller of `ISandboxProvider`.

For each sandbox tool call it:

1. Resolves which sandbox the calling bot means: its own, or one it names and is allowed to reach.
2. Checks the sharing policy against the bot tree.
3. Loads the sandbox row and its access token from the vault, inside the run's tenant transaction.
4. Builds the handle, calls the provider, and streams output into run events.

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

`sandbox.run` and the file tools take an optional `environment` argument naming a bot. The broker allows it when the calling bot is in that environment's sharing group, or is an ancestor of its owner and holds the `sandbox.enter` grant. A parent directing a child's work uses this. Siblings cannot, unless a `named` share says so.

### When a bot is removed

| Removed bot is | Policy | What happens |
| --- | --- | --- |
| Owner | `private` | Sandbox is retired |
| Owner | `subtree` | Its subtree is removed with it. Sandbox is retired. |
| Owner | `named` | Sandbox is retired. Other members lose access. |
| A member, not the owner | `subtree` or `named` | Its processes are stopped. The sandbox continues for the rest. The access token is rotated by recreating the handle, so nothing the removed bot started keeps a way in. |

"Retired" means: paused immediately, state `retiring`, kept for seven days so a human can download files or assign the environment to another bot, then destroyed. The retention period is a space setting and can be zero.

If a human moves a child out of a subtree before removing the parent, the child leaves the sharing group and gets its own private sandbox on next use.

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

All built-in, all granted per bot like any tool.

| Tool | Sensitivity |
| --- | --- |
| `sandbox.run` | write |
| `sandbox.read_file`, `sandbox.list_files` | read |
| `sandbox.write_file` | write |
| `sandbox.enter` (use another bot's environment) | critical |

Command, output, and exit code are run events, so the activity view shows a live terminal for each call.
