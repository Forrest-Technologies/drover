# 13. Deployment and push notifications

## Easy to stand up

Goal: clone, `docker compose up`, open the browser, chat. No file to edit.

### Compose services

| Service | Image | Role |
| --- | --- | --- |
| `init` | The same pinned PostgreSQL image as `db` | Runs once. Generates secrets onto the `drover-secrets` volume if absent, then exits. It uses the database image because that image is already pulled and has the shell tools needed; the `drover` image is built in the same `up` and cannot be ordered before it. |
| `db` | The official PostgreSQL image with the `pgvector` extension, pinned by digest | Database. Reads its password from the secrets volume. |
| `database-init` | Built from source | Runs `--initialize-database` once as root with bootstrap and owner secrets, then exits. |
| `drover` | Built from source | Serves API and web UI on localhost port 8080 as non-root, using only the application role. |

Optional pieces are off by default. `ntfy`, a self-hosted notification server for Android push, sits behind the `push` profile (`docker compose --profile push up`); Drover sends to it once push lands in Phase 1. The HTTPS reverse proxy is an override file, `compose.proxy.yaml`, rather than a profile, because it must change the `drover` service too: see [Reaching Drover from another device](#reaching-drover-from-another-device).

Every service drops every Linux capability and adds back only what its job needs, cannot gain privileges (`no-new-privileges`), and runs on a read-only root filesystem with `tmpfs` where it needs scratch space. `drover` runs as uid 1654 with no capability at all. `init` keeps `CHOWN`, `FOWNER`, and `DAC_OVERRIDE` to hand each password file to its reader; `database-init` keeps `DAC_READ_SEARCH` to read them; `db` keeps what the PostgreSQL image needs to drop from root to its own user. Every image from elsewhere is pinned by digest, and every port of `compose.yaml` is bound to `127.0.0.1`. `ComposeFileTests` in `Drover.Server.Tests` reads both compose files and fails the build when one of these rules breaks.

Start order is expressed with `depends_on` conditions: `db` waits for `init` to complete successfully, `database-init` waits for `db` to be healthy, and `drover` waits for initialization to complete successfully.

The project builds and hosts no images. `db` and the .NET base images are pulled from their publishers; `drover` is built locally.

### Secrets without a config file

`init` creates, once, with random values:

| File on `drover-secrets` | Used by |
| --- | --- |
| `postgres_password` | `db` through `POSTGRES_PASSWORD_FILE`; `database-init` for role creation only |
| `owner_password` | `database-init`, migration step |
| `app_password` | `drover`, request serving |

`init` creates these three files. The application generates a temporary setup code and logs it when there are no users. Each password file is written once and left alone on later starts, so `docker compose down` then `up` keeps the same secrets. The Data Protection key ring lives on a separate volume, `drover-keys`. The runtime mounts only `drover-app-secrets`, containing a copy of the app password readable by uid 1654. It never mounts the bootstrap or owner secrets. Existing secret permissions are corrected on each start without rotating values. Volumes: `drover-db`, `drover-secrets`, `drover-app-secrets`, `drover-keys`, `drover-files`.

### Database initialization and application startup

1. The one-shot `database-init` command waits for database health, then calls `DatabaseBootstrap` as the PostgreSQL superuser to create or reconcile the roles and grants in the existing `postgres` database.
2. It calls `DatabaseMigrator` as `drover_owner`, applying pending migrations, and exits. Bootstrap uses an advisory lock; EF Core serializes migrations with its migration lock. Both steps are safe to repeat.
3. Only after successful initialization does the non-root application start. It builds its connection as `drover_app` from `Database:Host`, optional port/database/timeout settings, and the application password: either a file named by `Database:AppPasswordFile` (Compose) or the value `Database:AppPassword` (environment variable `Database__AppPassword`, for a platform that gives settings only as variables). Both forms are checked alike: setting both, a missing file, or an empty value stops the server, and no message carries the value. `Database:SslMode` sets TLS for every connection the server and the initializer make: `Prefer` (the default: TLS when the database offers it, certificate not checked), `Require`, or `VerifyFull`. It performs no role or schema changes.
4. `ConnectionStrings:Drover` overrides the file-based connection for custom deployments and tests. Missing or empty connection fields fail startup. An explicit custom role is allowed only when the validated self-hosted policy permits migration credentials in the server. Hosted and tightened self-hosted policies require the actual low-privilege application role, verified against PostgreSQL before startup services access application data. File-based connections, including Compose, always use `drover_app`. Custom deployments must initialize their database separately.
5. Data Protection uses `DataProtection:KeyPath`, set to `/var/lib/drover/keys` in Compose. If there are no users, the server logs the setup address and code once it listens: the public address when one is set, otherwise each address it listens on, with a wildcard host printed as `localhost`.
6. At start, the server asks PostgreSQL which role its connection really is. A role that is a superuser or can bypass row-level security stops the server where the deployment keeps the migration credential out of it, and is logged as an error everywhere else.

Sandboxes are optional. To turn them on, give the server an E2B API key: as the value `Drover:Sandbox:E2B:ApiKey` (environment `Drover__Sandbox__E2B__ApiKey`), or as a file named by `Drover:Sandbox:E2B:ApiKeyFile`. `compose.yaml` has a commented example that mounts `./secrets/e2b_api_key` as a Compose secret at `/run/drover-sandbox/e2b_api_key` (outside `/run/secrets`, which holds the app password volume); the file must be readable by uid 1654. The key is read at start and held in memory only. A key without the `e2b_` prefix, both forms at once, or a missing file stops the server and names the setting; no key means no sandboxes. See [07-sandbox.md](07-sandbox.md#operator-key).

The initializer additionally requires the superuser and owner passwords, as files (`Database:PasswordFile`, `Database:OwnerPasswordFile`) or values (`Database:Password`, `Database:OwnerPassword`). Run it only as the standalone `--initialize-database` command; never give its secrets to the request-serving process. Where `AllowMigrationCredentialInServer` is off, a serving process given any of the four settings does not start; where it is on, the server starts and logs an error naming each one. Running on a platform that builds the image and fronts it with its own proxy is in [../platform-deploy.md](../platform-deploy.md).

`--reset-password person@example.com` is the other standalone command. It connects as the server does (`drover_app`, with the same deployment policy), prints a one-time password reset code for that account to the log, and exits with 1 when no account has the email. In compose: `docker compose run --rm drover --reset-password person@example.com`. See [08-auth-and-secrets.md](08-auth-and-secrets.md).

Upgrading is `git pull` to the new tag and the same `docker compose up --build`. The one-shot initialization command applies pending migrations before the application starts. Migrations are forward-only and written to be safe with existing data; a release that needs a manual step says so in its notes and refuses to start until it is done.

### First run in the browser

Three steps, one decision each: create the owner account, add a model connection (base address and key, with a test button), meet a starter bot. The starter bot is ordinary data, created through the same API as any bot, with no tools granted.

Everything optional stays off until configured. Each feature has a single "is it ready" check on the server, and the UI uses it to say what is missing in plain words: "Add a sandbox provider to let this bot run code."

### Status page

`/status` for a space's owners and admins (`GET /api/spaces/{spaceId}/status`), and `/health/live` and `/health/ready` for the container. Built on ASP.NET Core health checks, used directly.

`/health/ready` opens a fresh PostgreSQL connection using the same resolved application connection as the server. It touches no tenant data. The `drover` container's compose health check asks this endpoint, with bash over a plain socket, since the runtime image has no `curl`.

The page calls nothing to build itself: each row says what was last seen. Each row has a state said in words (working, needs attention, not working, off) and a sentence saying what to do when it is not working. The server's rows are shown to a space's owners and admins only where the deployment rule `SpaceAdminsSeeServerStatus` allows it: on by default for a self-hosted server, whose admins are its operator, and off in the hosted preset, where they are strangers.

| Row | Check |
| --- | --- |
| Database | Asks PostgreSQL which role the connection is; red when it is a superuser or can bypass row-level security |
| Migrations | The newest applied migration, read through `sys.applied_migration()`, equals the newest the build carries |
| Key ring | The folder is there and writable, how many keys it holds, and a reminder to back it up with the database |
| Public address | Whether `DROVER_PUBLIC_URL` is set, and a warning when it is plain HTTP on a non-loopback name. Tool server sign-ins and push need it. |
| Background work | Queued, running, and waiting runs, and runs whose worker lease expired, across every space as counts (`sys.health()`) |
| Sandboxes | Whether a sandbox provider key is configured: "configured" or "not configured" (off), and nothing else. It never names the provider or shows anything of the key. |
| Each model connection | Its state and when it was last checked |
| Each tool connection | Its state and when it last listed its tools; what the server said is on the Tools screen |

Not yet: a "test now" button per connection, whether the public address is reachable from outside, a worker heartbeat, a confirmed key ring backup, whether the sandbox provider answers, and rows for push, search, and voice, each of which arrives with its feature.

The first database row is worth its place: if someone points Drover at a superuser connection, isolation silently stops working. The status page turns red and the server logs an error at start.

### Reaching Drover from another device

`compose.yaml` binds the server to `127.0.0.1:8080`, so only the machine it runs on can open it. To reach it from a phone or another computer, put HTTPS in front:

```sh
DROVER_DOMAIN=drover.example.org docker compose -f compose.yaml -f compose.proxy.yaml up --build
```

`compose.proxy.yaml` adds a `proxy` service (Caddy, pinned by digest) that obtains and renews a certificate for `DROVER_DOMAIN`, redirects HTTP to HTTPS, and forwards to `drover:8080` with `X-Forwarded-For` and `X-Forwarded-Proto`. The same file gives the compose network a fixed subnet (`DROVER_PROXY_SUBNET`, default `172.30.80.0/24`) and the proxy a fixed address in it (`DROVER_PROXY_ADDRESS`, default `172.30.80.250`), and tells Drover to trust exactly that address (`Drover:Proxy:TrustedAddresses`) and that its public address is `https://DROVER_DOMAIN`. Drover then counts the real client for the sign-in limit, sees the request as HTTPS, and sends tool server sign-ins back to the right address. Nothing else on the network can claim an address or a scheme. The proxy publishes 80 and 443 on every interface; Drover's own port stays on `127.0.0.1`.

With a proxy of your own: set `DROVER_PUBLIC_URL` to the address people open, and `Drover__Proxy__TrustedAddresses__0` to the proxy's address or network as Drover sees it. Leaving the proxy untrusted still works, but every person then shares the proxy's address for the sign-in limit. Never turn on `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; the server refuses to start with it.

### Build cost

The image is built on the self-hoster's machine, so the build must fit a modest VPS. Design choices that keep it down:

- Multi-stage Dockerfile: SDK image to build, the ASP.NET runtime image to run. Only the published output is copied.
- The Docker build compiles `Drover.Server` and its references only. Tests, analyzers' tests, the AppHost, and the MAUI shell are not in the build context's project graph.
- No ahead-of-time compilation of the WebAssembly client, so no extra workload download. The client is trimmed.
- No Node, no CSS build tool.
- Restore is a separate layer keyed on `Directory.Packages.props` and the project files, so a rebuild after a small change skips it. (There are no lock files.)
- The build server and parallelism are limited inside the Dockerfile (`-maxcpucount:1`, shared compilation off) to cap peak memory at the cost of some time.

**Budget, enforced in CI:** the image must build inside a container limited to 2 GB of memory, and the job reports build time, image size, and the builder's peak memory on every run. BuildKit ignores a memory flag on `docker build`, so CI sets the limit on the builder container (`docker buildx create --driver-opt memory=2g`), which runs every build step; the peak is read from that container's cgroup and includes page cache. The README records the measured numbers. If the build cannot hold 2 GB, that is reported as an adoption risk ([12-risks.md](12-risks.md)), and the answer is not image hosting.

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
