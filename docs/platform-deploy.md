# Running on a platform that builds the image

Some servers run apps through a platform: it builds the app from the repository's `Dockerfile`, starts one container
per app, and serves it behind its own HTTPS proxy. Drover runs there without `docker compose`, but the platform must be
told to do the things the compose files do for you. This page lists them. The compose path
([architecture/13-deployment-and-push.md](architecture/13-deployment-and-push.md)) is unchanged and still the simplest.

No definition file is needed in the repository: the platform builds from the `Dockerfile` at the root, and the image
listens on port 8080.

## What you set up

Three apps on the platform, all from this repository or a pinned image:

| App | Image | Runs |
| --- | --- | --- |
| Database | The PostgreSQL image `compose.yaml` pins (`pgvector/pgvector`, PostgreSQL 17, by digest) | Always |
| Initialization | Built from the `Dockerfile`, command `--initialize-database` | Once at install, and once before each upgrade starts |
| Drover | Built from the `Dockerfile`, no command | Always |

Keep the initialization separate from the serving app. Its passwords can change roles and the schema; the serving
app never needs them, and should not hold them in case it is ever broken into. If the platform offers a "run once"
command on the serving app, check whether that command inherits the serving app's variables. If it does, put the
initialization passwords only on the one-off command, never on the app. A separate app or job on the same image is the
safer choice.

## 1. The database

Use the image `compose.yaml` pins, with the `pgvector` extension, rather than the platform's stock PostgreSQL. Give it
a persistent volume for `/var/lib/postgresql/data` and a superuser password (`POSTGRES_PASSWORD`).

Today no migration uses the extension, so a stock PostgreSQL works until long-term memory arrives. The migration that
adds it will create the `vector` extension; on an image without it, that one-off initialization run stops with
PostgreSQL's error that the extension is not available, and the status page then says the database is behind the
build. Moving a database to another image later means a dump and restore, so start on the right one.

### Encryption to the database

By default Drover uses TLS to PostgreSQL when the database offers it, without checking its certificate
(`Database__SslMode=Prefer`). That keeps out someone who only listens, not someone who can pretend to be the database.
It is enough when the database and Drover share a private network no other app joins. Otherwise set the same value on
both the one-off initialization and the serving app:

| `Database__SslMode` | Means |
| --- | --- |
| `Require` | Refuse a connection without TLS. The certificate is not checked. |
| `VerifyFull` | Refuse a connection without TLS, and check the certificate and the host name against the authorities the image trusts. |

The database must then serve a certificate. A certificate from your own private authority is not supported yet: the
image has no setting to trust it, so use `Require` there.

## 2. Passwords

Make two more long random passwords: one for the migration owner and one for the application. Store all three in the
platform's secret settings. Drover reads each password either as a value or as a file:

| Password | Value | File |
| --- | --- | --- |
| PostgreSQL superuser | `Database__Password` | `Database__PasswordFile` |
| Migration owner (`drover_owner`) | `Database__OwnerPassword` | `Database__OwnerPasswordFile` |
| Application (`drover_app`) | `Database__AppPassword` | `Database__AppPasswordFile` |

Give each password one way only: setting both forms stops the program, as do a missing file and an empty value.
Surrounding spaces and line breaks are removed from both forms. No log line, status row, health answer or error
message carries a password.

## 3. Initialization

Run the image once with the command `--initialize-database` and these variables:

| Variable | Value |
| --- | --- |
| `Database__Host` | The database app's name on the platform's network |
| `Database__Port` | Only if it is not 5432 |
| `Database__Password` | The superuser password |
| `Database__OwnerPassword` | The owner password |
| `Database__AppPassword` | The application password |
| `Database__SslMode` | Only to require TLS, see above |

It creates the roles, sets their passwords, applies every migration, and exits. It is safe to run again. Run it again
before each upgrade starts serving, so the new build finds its migrations applied.

## 4. The serving app

Run the image with no command and only these variables:

| Variable | Value |
| --- | --- |
| `Database__Host` | The database app's name |
| `Database__Port` | Only if it is not 5432 |
| `Database__AppPassword` | The application password, and no other |
| `Database__SslMode` | Only to require TLS, the same value as the initialization |
| `Drover__Policy__Rules__AllowMigrationCredentialInServer` | `false` |
| `DataProtection__KeyPath` | `/var/lib/drover/keys` |
| `DROVER_PUBLIC_URL` | The HTTPS address people open Drover at, such as `https://drover.example.org` |
| `Drover__Proxy__TrustedAddresses__0` | The platform proxy's network, see below |

Do not give it `Database__Password`, `Database__OwnerPassword` or their file forms. These step around the rules that
keep one space's data from another, and the serving app is the process facing the internet. If it is given any of
them, it logs an error at start naming each one. With `AllowMigrationCredentialInServer` set to `false`, as above, it
refuses to start instead, so a password that leaks into the app's variables (for example from a "run once" command
that shares them) cannot go unnoticed. That setting also makes the server check at start that it connects as
`drover_app` and nothing more powerful. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: the server refuses to start
with it on.

Volumes, both writable by the image's user:

| Path | Holds |
| --- | --- |
| `/var/lib/drover/keys` | The key ring. Without it, every stored key and password is unreadable. Back it up with the database. |
| `/var/lib/drover/files` | Stored files. Mount it now so an upgrade does not need it added. |

The image runs as a non-root user (uid 1654) and owns these paths. Leave the user as it is. If the platform lets you,
also make the root file system read-only (with `/tmp` writable), drop every Linux capability, and set
`no-new-privileges`, as `compose.yaml` does.

Readiness is `GET /health/ready` (the database answers as `drover_app`) and liveness is `GET /health/live`.

On first start with no accounts, the server logs a one-time setup code. Open the public address and enter it.

## 5. The proxy

The platform's proxy ends HTTPS and passes the request on, saying in `X-Forwarded-For` and `X-Forwarded-Proto` who
the client is and that the request came over HTTPS. Drover believes those headers only from an address you name, so
nobody else can claim to be someone they are not. Without it, every person shares the proxy's address for the sign-in
limit, and the server thinks every request came over plain HTTP.

A platform's proxy usually reaches the app over an internal network from an address that changes between restarts.
Name that network in CIDR form instead of one address, for example `Drover__Proxy__TrustedAddresses__0=10.0.1.0/24`.
More entries go in `__1`, `__2`, and so on, and exact addresses still work. To find the network, look at the
platform's network settings, or at the address a request arrives from in the app's own network.

Every address in a trusted range can claim to be any client and to have used HTTPS. If a range also holds other apps,
a broken or hostile app there can make its requests look as if they came from anyone, and get past the sign-in limit.
In order of preference:

1. Put the proxy and Drover on a network no other app joins, and name that network.
2. If the proxy's address does not change, name that one address.
3. Only otherwise, name the narrowest range that holds the proxy, knowing that every app in it is trusted as much.

The server refuses a range wider than /8 for IPv4 or /32 for IPv6, a range written with a host address such as
`10.0.1.7/24`, an IPv6 range that takes in IPv4 addresses (write those as the IPv4 range), and anything not written in
plain decimal form (`010.0.0.0/8`), and says why.

### More than one proxy in front

Drover believes one proxy: the one that reaches it. If another proxy sits in front of the platform's, such as a content
network, Drover sees the outer proxy's address as the client. Every person then shares one sign-in allowance (by default
10 failed attempts a minute), so anyone failing to sign in can keep everyone else, the owner included, from signing in.
This is not supported yet. Until it is, let people reach the platform's proxy directly, with nothing in front of it.


## Checking it

1. The serving app becomes ready, and its log shows no error about the database role.
2. The status page shows the database connected as `drover_app`, migrations up to date, the key ring writable, and
   the public address on HTTPS.
3. Sign-in works over the public address, and failed sign-ins by two different people count against separate limits.
