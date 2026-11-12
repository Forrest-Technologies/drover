# 13. Deployment and push notifications

## Easy to stand up

Goal: clone, `docker compose up`, open the browser, chat. No file to edit.

### Compose services

| Service | Image | Role |
| --- | --- | --- |
| `init` | The same pinned PostgreSQL image as `db` | Runs once. Generates secrets onto the `drover-secrets` volume if absent, then exits. It uses the database image because that image is already pulled and has the shell tools needed; the `drover` image is built in the same `up` and cannot be ordered before it. |
| `db` | The official PostgreSQL image with the `pgvector` extension, pinned by digest | Database. Reads its password from the secrets volume. |
| `drover` | Built from source | Migrates, then serves API, web UI, streams, and background work on port 8080, bound to localhost. Until the migration step exists it serves the page and the health endpoints. |

Optional services sit behind compose profiles and are off by default: `ntfy` for self-hosted Android push, and a reverse proxy example for HTTPS.

Start order is expressed with `depends_on` conditions: `db` waits for `init` to complete successfully, `drover` waits for `db` to be healthy.

The project builds and hosts no images. `db` and the .NET base images are pulled from their publishers; `drover` is built locally.

### Secrets without a config file

`init` creates, once, with random values:

| File on `drover-secrets` | Used by |
| --- | --- |
| `postgres_password` | `db` through `POSTGRES_PASSWORD_FILE`; `drover` for first-time role creation only |
| `owner_password` | `drover`, migration step |
| `app_password` | `drover`, request serving |
| `setup_code` | Shown in the log; asked by the setup screen for remote first-run |

Until the setup screen exists, `init` creates the first three. Each file is written once and left alone on later starts, so `docker compose down` then `up` keeps the same secrets. The Data Protection key ring lives on a separate volume, `drover-keys`. Volumes: `drover-db`, `drover-secrets`, `drover-keys`, `drover-files`.

### What `drover` does at start

1. Wait for the database.
2. First start only: connect as the PostgreSQL superuser, create the three roles and the database, hand ownership to `drover_owner`.
3. Connect as `drover_owner` and apply EF Core migrations. Close that connection.
4. Open the application pool as `drover_app` and start serving.
5. If there are no users, log the setup address and code.

A PostgreSQL advisory lock around steps 2 and 3 makes two instances starting together safe.

Upgrading is `git pull` to the new tag and the same `docker compose up --build`. Migrations run in step 3. Migrations are forward-only and written to be safe to run while the previous version's data is in place; a release that needs a manual step says so in its notes and refuses to start until it is done.

### First run in the browser

Three steps, one decision each: create the owner account, add a model connection (base address and key, with a test button), meet a starter bot. The starter bot is ordinary data, created through the same API as any bot, with no tools granted.

Everything optional stays off until configured. Each feature has a single "is it ready" check on the server, and the UI uses it to say what is missing in plain words: "Add a sandbox provider to let this bot run code."

### Status page

`/status` for admins, and `/health/live` and `/health/ready` for the container. Built on ASP.NET Core health checks, used directly.

Until the status page exists, `/health/ready` runs one check: it can open a connection to PostgreSQL (`Database:Host`, `Database:PasswordFile`). It runs no query, because there is no schema yet. The `drover` container's compose health check asks this endpoint, with bash over a plain socket, since the runtime image has no `curl`.

| Row | Check |
| --- | --- |
| Database | Connects as `drover_app`; confirms the role cannot bypass row-level security |
| Migrations | Applied version equals the build's version |
| Each model connection | Last call result and time; a "test now" button |
| Each tool connection | Last list result, credential state |
| Sandbox provider, push, search, voice | Configured or not, last result |
| Background work | Queued runs, oldest waiting run, worker heartbeat, expired leases |
| Key ring | Present, writable, and whether the operator has confirmed a backup |
| Public address | Whether `DROVER_PUBLIC_URL` is set and reachable, since OAuth connections and push need it |

The first database row is worth its place: if someone points Drover at a superuser connection, isolation silently stops working. The status page turns red and the server logs an error at start.

### Build cost

The image is built on the self-hoster's machine, so the build must fit a modest VPS. Design choices that keep it down:

- Multi-stage Dockerfile: SDK image to build, the ASP.NET runtime image to run. Only the published output is copied.
- The Docker build compiles `Drover.Server` and its references only. Tests, analyzers' tests, the AppHost, and the MAUI shell are not in the build context's project graph.
- No ahead-of-time compilation of the WebAssembly client, so no extra workload download. The client is trimmed.
- No Node, no CSS build tool.
- Restore is a separate layer keyed on `Directory.Packages.props` and the project files, so a rebuild after a small change skips it. (There are no lock files.)
- The build server and parallelism are limited inside the Dockerfile (`-maxcpucount:1`, shared compilation off) to cap peak memory at the cost of some time.

**Budget, enforced in CI:** the image must build inside a container limited to 2 GB of memory, and the job reports build time, image size, and the builder's peak memory on every run. BuildKit ignores a memory flag on `docker build`, so CI sets the limit on the builder container (`docker buildx create --driver-opt memory=2g`), which runs every build step; the peak is read from that container's cgroup and includes page cache. These are budgets to hold, not measurements; there is no code to measure yet. Phase 0 step 15 records the first real numbers in the README. If the build cannot hold 2 GB, that is reported as an adoption risk ([12-risks.md](12-risks.md)), and the answer is not image hosting.

### Local development

`dotnet run --project src/Drover.AppHost` starts PostgreSQL and the server together through Aspire, with the dashboard for logs and traces. Aspire is development-time only (verified: it is not a production runtime and does not replace a compose file). Nothing that ships references it.

---

## Push notifications

### Principle

A push is a wake-up. Its payload is `{ "kind": "...", "thread": "<id>" }` and nothing else. The app wakes, calls the user's own server with its own credentials, and fetches what to show. No relay or push service ever sees message text, bot names, or titles.

### Interface

```csharp
namespace Drover.Domain.Push;

public interface IPushChannel
{
    string Kind { get; }

    Task<PushOutcome> Send(PushSubscription subscription, PushWakeUp wakeUp, CancellationToken cancellationToken);
}


public sealed record PushWakeUp(string Kind, Guid ThreadId);


public enum PushOutcome
{
    Delivered,
    SubscriptionGone,
    RetryLater,
}
```

`SubscriptionGone` deletes the subscription. `RetryLater` is retried with backoff by the notifier. The notifier decides when to push: a run finished or needs approval, and the user has no live stream open.

### Adapters

| Target | Adapter | Account needed | Phase |
| --- | --- | --- | --- |
| Browsers, installed PWA | Web Push | None | 1 |
| Android, through a UnifiedPush distributor or the embedded distributor | **The same Web Push adapter** | None | 1 |
| iPhone and iPad, as an installed web app | The same Web Push adapter | None | 1, see below |
| iOS native app | Apple relay | Paid Apple developer account | 2, last |
| Android store build | Google's push service | Google account | Later |

**One adapter covers every target that needs no publisher account.** UnifiedPush's server side is plain Web Push: an encrypted POST to the subscription's endpoint (verified against the current UnifiedPush specification). Some distributors require VAPID, so the adapter always signs. The library is `Lib.Net.Http.WebPush` (MIT). VAPID keys are generated at first start and stored in the vault.

### Android client

UnifiedPush stays. Alternatives were checked (the app holding its own connection in a foreground service, publishing to a notification app's topics, Google's push service directly, web push inside the app's web view) and none is a better open option: each either costs battery and a permanent notification, does not wake the Drover app, needs the publisher's credentials and a relay, or is not supported by the Android web view (verified).

What changes is how the app speaks it. There is no .NET or MAUI binding for the official connector, and the connector pulls in a Kotlin runtime and a crypto library. The protocol itself is small, so Drover implements it directly in the Android platform project:

| Piece | What it is |
| --- | --- |
| Registration | Three broadcasts to the distributor: register, unregister, acknowledge |
| Receiver | One exported broadcast receiver for: new endpoint, message, unregistered, registration failed |
| Distributor choice | One activity call that lets the user pick among installed distributors |
| Decryption | The Web Push payload, using the platform's elliptic-curve and AES-GCM primitives. No extra library. |

A few hundred lines of C#, all in `Drover.App/Platforms/Android`, behind `IPushRegistration`. One other .NET app is known to do the same, which shows it is feasible.

The app picks a path at run time, in this order:

| # | Path | When | User effort | Google involved |
| --- | --- | --- | --- | --- |
| 1 | **A UnifiedPush distributor app** (such as ntfy, or one backed by a browser vendor's push service) | One is installed | Install one app once, allow it to run in the background | No |
| 2 | **Embedded distributor over the phone's built-in push transport** | No distributor installed, and Google Play services is present | None | Transport only. No Google account, no Firebase project, no proprietary library: it registers a standard Web Push endpoint using the server's own VAPID key (verified in the distributor's documentation). |
| 3 | **In-app connection** with a foreground service | Neither of the above, and the user opts in | A permanent notification and a battery exemption | No |

Path 2 is the important find: most Android phones get working push with zero setup, and the server still needs only its Web Push adapter. It is an Apache-2.0 library added as an unbound Android archive with one manifest entry. A build flag leaves it out for a strictly Google-free build.

If no path is available, the app says so in plain words and offers a one-tap link to install a distributor.

Order of work in Phase 1: path 1 first (pure C#, no dependency), then path 2 (prototype early, since "unbound archive with no C# calls" is inferred from documentation, not yet tried). Path 3 is built only if users ask.

### iPhone before the Apple account exists

Web Push works for web apps added to the home screen on iOS and iPadOS, with no Apple developer membership (verified against the browser engine's own announcements). So an iPhone user can install the Drover PWA and receive pushes in Phase 1, through the adapter that already exists. This does not replace the native app, but it means iOS is not a dead end while the Apple account is pending.

### Apple relay (designed now, built last)

Apple's push service authenticates the app's developer key. A self-hosted server cannot hold that key, because the key belongs to whoever publishes the app. So native iOS push needs a small relay run by the app's publisher.

| Aspect | Design |
| --- | --- |
| What it is | A separate small open source service, `Drover.PushRelay`, in the same repository, deployable on its own |
| What it holds | The Apple signing key for the published app. Nothing else. |
| What it receives | `{ device_token, wake_up }` from a Drover server. The wake-up is the same two-field payload. |
| What it does | Signs a provider token, forwards a background notification to Apple over HTTP/2, returns the outcome |
| What it never sees | Message content, user identity, server address beyond the connection itself |
| Abuse control | Each Drover server registers once and gets a relay credential; per-server and per-device rate limits; device tokens are only useful for the Drover app |
| Apple client | A small HTTP/2 client with a signed token, owned in `Drover.Adapters.Apns`. The maintained libraries either pull in old dependencies or bring a larger surface than the one call needed. |

On the Drover server it is one more `IPushChannel` that posts to the relay. A self-hoster who builds their own iOS app can run their own relay with their own key.

### Client registration

`IPushRegistration` in `Drover.Ui` has a web implementation (service worker and the Push API) and MAUI implementations per platform. It asks for permission only after the user turns notifications on, never at first launch, and explains what the notification will and will not contain.
