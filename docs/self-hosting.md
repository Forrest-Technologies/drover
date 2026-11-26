# Self-hosting Drover

This guide covers running Drover on a machine you control: reaching it from other devices, setting its public address, backing up, restoring, and moving to another machine. For the first start, see the [README](../README.md#quick-start).

## What has been tested

| Part | State |
| --- | --- |
| Start, restart, and data kept across restarts | Tested on macOS with Docker |
| Setup, chat, the bot editor, tool connections with a header | Tested on macOS with Docker |
| A live model provider key | Not verified for this guide |
| A tool server sign-in | Not verified for this guide |
| The HTTPS proxy on a real domain | Not verified for this guide |
| Backup and restore as written below | Not verified for this guide |

Where a section below covers an untested part, it says so.

## What runs

`docker compose up --build` starts `db` (PostgreSQL), two one-shot services that prepare secrets and the database, and `drover`. It listens on `127.0.0.1:8080` only. The first start prints a setup address and a one-time setup code in the `drover` log:

```sh
docker compose logs drover
```

The image serves the browser client at `/`. Until 2026-11-20 it was served at `/native/`; that address now sends the
browser to the same address at `/`. An app installed from `/native/` is moved to `/` the next time it is opened and
keeps its own window. Notifications a browser had on at `/native/` are turned on again at `/` the next time the
person signs in, when the browser still allows them; otherwise Drover says to turn them on again. `DROVER_BUILD_NATIVE` no
longer exists: the image always includes the browser client.

## Reaching Drover from other devices

`compose.yaml` binds Drover to this machine only. To open it from a phone or another computer, put HTTPS in front with the proxy overlay.

1. Point a domain name at the machine.
2. Open ports 80 and 443 to it. The proxy needs them to obtain a certificate.
3. Start both files together:

```sh
DROVER_DOMAIN=drover.example.org docker compose -f compose.yaml -f compose.proxy.yaml up --build
```

To enable HTML previews, point a second DNS name at the same proxy and add the preview override:

```sh
DROVER_DOMAIN=drover.example.org DROVER_PREVIEW_DOMAIN=drover-preview.example.net \
  docker compose -f compose.yaml -f compose.proxy.yaml -f compose.preview.yaml up --build
```

Replace both names with names you control. The preview name must be on a separate site, such as `example.net` when
the application is on `example.org`. This boundary keeps bot-written HTML away from application cookies. Put both
values in a file named `.env` beside `compose.yaml` to keep them between runs. The server conservatively compares
the final two DNS labels. Choose names with different final two labels if a multi-label public suffix causes rejection.

The overlay adds a `proxy` service that gets and renews the certificate, redirects HTTP to HTTPS, and forwards to Drover. It gives the compose network a fixed subnet (`DROVER_PROXY_SUBNET`, default `172.30.80.0/24`) and the proxy a fixed address in it (`DROVER_PROXY_ADDRESS`, default `172.30.80.250`). Drover trusts the forwarded client address and scheme from that address only. If the default subnet clashes with another network on the machine, set both variables.

The overlay was not tested on a real domain for this guide. If the certificate is not issued, read the proxy log with `docker compose -f compose.yaml -f compose.proxy.yaml logs proxy`.

### A proxy of your own

Skip the overlay and set two things on the `drover` service:

| Setting | Value |
| --- | --- |
| `DROVER_PUBLIC_URL` | The address people open, such as `https://drover.example.org` |
| `DROVER_PREVIEW_URL` | A separate HTTPS site used only for untrusted HTML previews, such as `https://drover-preview.example.net` |
| `Drover__Proxy__TrustedAddresses__0` | The proxy's address or network, as Drover sees it |

Route both HTTPS names to the Drover server. Preview routes are refused on the application name. Application and API
routes are refused on the preview name. If `DROVER_PREVIEW_URL` is absent, file downloads continue to work and HTML
preview explains what the operator must configure. Do not record preview query strings in proxy access logs. The
provided overlay discards access logs for the preview name because its query carries the one-use grant.

Without the trusted-address setting Drover still works, but every person shares the proxy's address for the sign-in
limit. Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; the server refuses to start with it.

## Sandboxes

A sandbox is an isolated computer in which a bot can run code, keep files and drive a screen. Sandboxes are off until you give the server a provider; bots without one work as before.

### Docker

Docker sandboxes need no account and no steps. Their setup is written by [#657](https://github.com/Forrest-Technologies/drover/issues/657), with the hardening and provider settings from [#658](https://github.com/Forrest-Technologies/drover/issues/658) and [#659](https://github.com/Forrest-Technologies/drover/issues/659).

### E2B

E2B runs each sandbox as a remote machine at the provider. You need an account there, and the provider bills you for the time sandboxes run; a sandbox with a screen is a much larger machine than one without ([sandboxes](architecture/07-sandbox.md)).

1. Create an API key at the provider.
2. Put `E2B_API_KEY=e2b_...` in `.env` beside `compose.yaml` and run `docker compose up -d`. The key is held in memory only; `.env` is not tracked by git.

If you need more than that one line, these settings go beside it on the `drover` service:

| Setting | What it does |
| --- | --- |
| `Drover__Sandbox__E2B__ApiKeyFile` | Use a file holding the key instead of the value above. Never set both; the server stops at start if you do. |
| `Drover__Sandbox__E2B__Domain` | The provider's domain. Default `e2b.app`. |
| `Drover__Sandbox__E2B__Template` | The machine a sandbox starts from. Default `base`. |
| `Drover__Sandbox__E2B__DesktopTemplate` | The machine a sandbox with a screen starts from. Default `desktop`. |

The server stops at start, naming the setting, when a key does not start with `e2b_`, when both key settings are set, or when the key file is not readable. No key means no sandboxes, which is not an error.

### Other providers

Daytona ([#660](https://github.com/Forrest-Technologies/drover/issues/660)) and Novita ([#141](https://github.com/Forrest-Technologies/drover/issues/141)) each add their own subsection when they land.

### After it is on

Turn Sandbox on in the bot's editor, then grant the sandbox tools the bot may use. The bot's sandbox page is at `/bots/{botId}/sandbox`. Self-hosted limits are `MaxSandboxCommandSeconds` (3600), `MaxSandboxFileBytes` (50 MB) and `MaxSandboxIdleMinutes` (60); what they bound is in [deployment policies](architecture/16-deployment-policies.md). To turn sandboxes off again, remove `E2B_API_KEY` from `.env` and run `docker compose up -d`; an empty value means no provider is registered. Nothing then clears sandboxes that already exist: with no key the server registers no sandbox provider, no sandbox tools and no sweep, so the rows stay in Drover and the sandboxes stay at the provider with their files until someone removes them in the provider's own console; check the provider's pricing for paused sandboxes. Leave the key in until they have been cleared if you want the server's sweep to handle them. [Issue 649](https://github.com/Forrest-Technologies/drover/issues/649) covers sandboxes left at a previous provider.

## Before you expose it

- Finish setup before making Drover available over the internet. Read the one-time code from `docker compose logs drover`.
- Leave open registration off for a private server.
- Set `Drover__Policy__Rules__AllowPrivateAddresses=false` on the `drover` service if you do not need model and tool connections to private networks. Drover refuses to start with open registration and private addresses both enabled.
- Keep database and key ring backups private. Together they can be used to read stored secrets.

## The public address

`DROVER_PUBLIC_URL` is the address people open Drover at. It is empty by default, which is fine on one machine. Tool server sign-ins need it, because the tool server sends the person back to this address. The overlay sets it for you from `DROVER_DOMAIN`.

The status page (`/status`, for admins) has a row for it. It warns when the address is plain HTTP on a name other than this machine.

## Signing in to a model provider

Signing in with a person's own account in place of a key is being added one provider at a time ([#526](https://github.com/Forrest-Technologies/drover/issues/526)); the server side is in place and no provider is offered yet. Each person signs in for themself, and a bot uses that account only for work it does for that person ([model providers](architecture/15-model-providers.md#signing-in-with-a-subscription)). On a self-hosted server every sign-in kind is on with nothing to set. To switch one off without a new release, list its kind on the `drover` service: `Drover__Models__SignInOff__0=<kind>`.

## Backup

Back up four volumes together. Compose prefixes volume names with the project name, so list them first:

```sh
docker volume ls --filter name=drover
```

Look for `drover_drover-db`, `drover_drover-keys`, `drover_drover-files`, and `drover_drover-secrets`.

| Volume | Holds |
| --- | --- |
| `drover-db` | The database: accounts, bots, threads, connections |
| `drover-keys` | The key ring that encrypts every stored model key, tool header, and sign-in |
| `drover-files` | Files that bots and people add |
| `drover-secrets` | The database passwords. A physical database restore needs the original PostgreSQL password. |

A copy of the database without `drover-keys` cannot be read for stored secrets. Keep the key ring copy somewhere other than where the database copy lives.

`drover-app-secrets` is generated again from `drover-secrets` on startup. Keep a copy of any Compose overrides, `.env` settings, and separately mounted secrets needed on the new machine.

Stop the stack so the copies are consistent, then copy each volume to an archive. Use the same Compose project name throughout. The commands default to `drover`. Set `COMPOSE_PROJECT_NAME` first if you use another name. Run from the checkout and use the same Compose files you started with.

```sh
(
  set -eu
  export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-drover}"
  umask 077
  backup_dir=$(mktemp -d "$HOME/drover-backup.XXXXXX")
  printf 'Backup directory: %s\n' "$backup_dir"
  docker compose down
  for volume in drover-db drover-keys drover-files drover-secrets; do
    source_volume="${COMPOSE_PROJECT_NAME}_${volume}"
    docker volume inspect "$source_volume" > /dev/null
    docker run --rm -v "$source_volume:/data:ro" alpine tar czf - -C /data . > "$backup_dir/${volume}.tar.gz"
    tar tzf "$backup_dir/${volume}.tar.gz" > /dev/null
  done
  docker compose up -d
)
```

`docker compose down` keeps the volumes. Keep the archives together in encrypted storage.

## What is lost if the key volume is lost

Every stored secret becomes unreadable: model keys, tool server headers, tool server sign-ins, and client secrets. Threads, bots, and settings stay in the database.

To recover, start Drover and enter each model key and tool credential again. There is no way to recover the old values.

## Restore onto a fresh machine

1. Install Docker with Compose, check out the same version, and copy the four archives to a private directory outside the checkout. Set `DROVER_BACKUP_DIR` to its absolute path.
2. Apply your deployment settings. Run the commands below with the same Compose files and project name you will use to start Drover. The project name must be unused on the destination machine.

```sh
(
  set -eu
  export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-drover}"
  : "${DROVER_BACKUP_DIR:?Set DROVER_BACKUP_DIR to the archive directory}"
  for volume in drover-db drover-keys drover-files drover-secrets; do
    if docker volume inspect "${COMPOSE_PROJECT_NAME}_${volume}" > /dev/null 2>&1; then
      echo "Restore requires a new volume for ${volume}." >&2
      exit 1
    fi
    tar tzf "$DROVER_BACKUP_DIR/${volume}.tar.gz" > /dev/null
  done

  docker compose create --build
  for volume in drover-db drover-keys drover-files drover-secrets; do
    target_volume="${COMPOSE_PROJECT_NAME}_${volume}"
    docker volume inspect "$target_volume" > /dev/null
    docker run --rm -i -v "$target_volume:/data" alpine \
      sh -eu -c 'test -z "$(ls -A /data)"; tar xzpf - -C /data' < "$DROVER_BACKUP_DIR/${volume}.tar.gz"
  done
  docker compose up -d
)
```

3. Open `/status`, sign in, and check a thread with an attachment and a model or tool connection that uses a stored credential.

A full restore is tracked in [#17](https://github.com/Forrest-Technologies/drover/issues/17).

## Moving to another machine

Moving is a backup on the old machine and a restore on the new one. Use the same version on both. If the address changes, set `DROVER_PUBLIC_URL` (or `DROVER_DOMAIN`) to the new one, and sign in to tool servers again if they signed in against the old address.

## Upgrading

Check out a newer tag or commit and run the same `docker compose up --build`. Pending database migrations apply before the server starts. Take a backup first. If a release needs a manual step, its notes say so.

`docker compose down` keeps your data. `docker compose down --volumes` deletes every volume, including the key ring.

## Related

- [Deployment design](architecture/13-deployment-and-push.md)
- [Secrets and the key ring](architecture/08-auth-and-secrets.md)
