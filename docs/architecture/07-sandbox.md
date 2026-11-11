# 7. Sandboxes

A sandbox is an isolated computer a bot can work in: files, processes, network. Phase 1.

## The interface

```csharp
namespace Drover.Domain.Sandboxes;

public interface ISandboxProvider
{
    string Kind { get; }

    Task<SandboxHandle> Create(SandboxSpec spec, CancellationToken cancellationToken);

    Task Pause(SandboxHandle sandbox, CancellationToken cancellationToken);

    Task Resume(SandboxHandle sandbox, CancellationToken cancellationToken);

    Task Destroy(SandboxHandle sandbox, CancellationToken cancellationToken);

    Task<SandboxState> GetState(SandboxHandle sandbox, CancellationToken cancellationToken);

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

    public required Guid SharingGroupId { get; init; }

    public required string Image { get; init; }

    public required NetworkPolicy Network { get; init; }

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);
}


public sealed record SandboxHandle(string ProviderSandboxId, Guid SharingGroupId, string AccessToken);
```

All types are Drover's. `Run` streams output so a long command shows live in the activity view. `SandboxHandle` is a capability: it carries the per-sandbox access token, and only the broker can build one.

The conformance suite runs every adapter through: create, run a command, stream output, write and read a file, deny network, pause and resume with a file surviving, destroy, and "a handle for sandbox A cannot operate on sandbox B".

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
| 2 | **A per-sandbox access token**, issued by the provider at creation and stored in the vault as a space secret. The adapter cannot operate on a sandbox without it. | Knowing or guessing another sandbox's id is not enough. A bug that passes the wrong id gets an authentication failure from the provider. |
| 3 | **Provider-side metadata check.** Each sandbox is created with metadata `drover.space` and `drover.group`. Before use, the adapter reads the sandbox's metadata from the provider and refuses if it does not match the handle. | The check is against the provider's record, not Drover's database, so a corrupted or confused row cannot redirect a bot into another group's machine. |
| 4 | **Network policy at the provider.** Sandboxes cannot reach each other. Egress follows the bot's sandbox settings: off, allowlist, or open. | A process in one sandbox cannot connect to another, or to the Drover server's internal addresses. |

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

Verified facts this adapter relies on:

- No official .NET SDK. Official SDKs are JavaScript and Python.
- A REST control plane with an API key header: create, list, get, kill, pause, resume, set timeout, set network rules.
- An in-sandbox daemon reached over HTTPS with a per-sandbox access token: REST for files, and an RPC protocol with JSON for processes and the filesystem.
- The OpenAPI document and the proto files are published in E2B's repository.
- Egress allow and deny rules, updatable on a running sandbox. Public inbound traffic can be turned off.
- Pause keeps a sandbox indefinitely; resume resets its runtime limit. Continuous runtime is capped by plan.
- The runtime is Apache-2.0 and can be self-hosted.

Design:

| Part | Approach |
| --- | --- |
| Control plane client | A thin typed `HttpClient` class for the eight or so endpoints used, written against the published OpenAPI document. Request and response records are internal to the adapter. |
| Process and filesystem client | A small client for the daemon's RPC: JSON request, length-prefixed streamed frames for output. The message shapes come from the published proto files. |
| Lifecycle | Create on first use. Auto-pause when idle; resume on next use. Drover does not keep sandboxes running between runs. |
| Mapping | `SharingGroupId` and `SpaceId` go into sandbox metadata. The provider's access token becomes `SandboxHandle.AccessToken`. |
| Network | `NetworkPolicy` maps to internet on or off plus allow and deny lists. Inbound public traffic is off. |
| Tests | The conformance suite runs against recorded HTTP fixtures in pull request CI (deterministic, offline) and against the live service in a manually triggered job with a key from repository secrets. |

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
