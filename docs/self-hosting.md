# Self-hosting Drover

This guide covers running Drover on a machine you control: reaching it from other devices, setting its public address, backing up, restoring, and moving to another machine. For the first start, see the [README](../README.md#quick-start).

## What has been tested

| Part | State |
| --- | --- |
| Start, restart, and data kept across restarts | Tested on macOS with Docker |
| Setup, chat, the bot editor, tool connections with a header | Tested on macOS with Docker |
| A live model provider key | Not tested by anyone yet |
| A tool server sign-in | Not tested by anyone yet |
| The HTTPS proxy on a real domain | Not tested by anyone yet |
| Backup and restore as written below | Not tested by anyone yet |

Where a section below covers an untested part, it says so.

## What runs

`docker compose up --build` starts `db` (PostgreSQL), two one-shot services that prepare secrets and the database, and `drover`. It listens on `127.0.0.1:8080` only. The first start prints a setup address and a one-time setup code in the `drover` log:

```sh
docker compose logs drover
```

## Reaching Drover from other devices

`compose.yaml` binds Drover to this machine only. To open it from a phone or another computer, put HTTPS in front with the proxy overlay.

1. Point a domain name at the machine.
2. Open ports 80 and 443 to it. The proxy needs them to obtain a certificate.
3. Start both files together:

```sh
DROVER_DOMAIN=drover.example.org docker compose -f compose.yaml -f compose.proxy.yaml up --build
```

Replace `drover.example.org` with your own name. Put `DROVER_DOMAIN=...` in a file named `.env` beside `compose.yaml` to keep it between runs.

The overlay adds a `proxy` service that gets and renews the certificate, redirects HTTP to HTTPS, and forwards to Drover. It gives the compose network a fixed subnet (`DROVER_PROXY_SUBNET`, default `172.30.80.0/24`) and the proxy a fixed address in it (`DROVER_PROXY_ADDRESS`, default `172.30.80.250`). Drover trusts the forwarded client address and scheme from that address only. If the default subnet clashes with another network on the machine, set both variables.

This overlay has not been run on a real domain by anyone yet. If the certificate is not issued, read the proxy log with `docker compose -f compose.yaml -f compose.proxy.yaml logs proxy`.

### A proxy of your own

Skip the overlay and set two things on the `drover` service:

| Setting | Value |
| --- | --- |
| `DROVER_PUBLIC_URL` | The address people open, such as `https://drover.example.org` |
| `Drover__Proxy__TrustedAddresses__0` | The proxy's address or network, as Drover sees it |

Without the second setting Drover still works, but every person shares the proxy's address for the sign-in limit. Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; the server refuses to start with it.

## The public address

`DROVER_PUBLIC_URL` is the address people open Drover at. It is empty by default, which is fine on one machine. Tool server sign-ins need it, because the tool server sends the person back to this address. The overlay sets it for you from `DROVER_DOMAIN`.

The status page (`/status`, for admins) has a row for it. It warns when the address is plain HTTP on a name other than this machine.

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
| `drover-secrets` | The database passwords. Optional: a fresh machine makes new ones and resets the database roles to match. |

A copy of the database without `drover-keys` cannot be read for stored secrets. Keep the key ring copy somewhere other than where the database copy lives.

Stop the stack so the copies are consistent, then copy each volume to an archive:

```sh
docker compose down
for volume in drover-db drover-keys drover-files drover-secrets; do
  docker run --rm -v "drover_${volume}:/data:ro" -v "$PWD:/backup" alpine \
    tar czf "/backup/${volume}.tar.gz" -C /data .
done
docker compose up -d
```

`docker compose down` keeps the volumes. These commands were not run in the environment that wrote this guide.

## What is lost if the key volume is lost

Every stored secret becomes unreadable: model keys, tool server headers, tool server sign-ins, and client secrets. Threads, bots, and settings stay in the database.

To recover, start Drover and enter each model key and tool credential again. There is no way to recover the old values.

## Restore onto a fresh machine

1. Install Docker with Compose and clone the repository at the same version you ran before.
2. Create empty volumes by starting once and stopping: `docker compose up --build -d` then `docker compose down`.
3. Put each archive back:

```sh
for volume in drover-db drover-keys drover-files; do
  docker run --rm -v "drover_${volume}:/data" -v "$PWD:/backup" alpine \
    sh -c "rm -rf /data/* /data/.[!.]* ; tar xzf /backup/${volume}.tar.gz -C /data"
done
```

4. Start: `docker compose up --build -d`. The initialization step sets the database passwords to the ones this machine created, so you do not need to restore `drover-secrets`.

Check `/status` and sign in. Threads should be there, and model and tool connections should report as working.

Nobody has run this restore yet. The first person who does should note any difference on the tracking issue.

## Moving to another machine

Moving is a backup on the old machine and a restore on the new one. Use the same version on both. If the address changes, set `DROVER_PUBLIC_URL` (or `DROVER_DOMAIN`) to the new one, and sign in to tool servers again if they signed in against the old address.

## Upgrading

Check out a newer tag or commit and run the same `docker compose up --build`. Pending database migrations apply before the server starts. Take a backup first. If a release needs a manual step, its notes say so.

`docker compose down` keeps your data. `docker compose down --volumes` deletes every volume, including the key ring.

## Related

- [Deployment design](architecture/13-deployment-and-push.md)
- [Secrets and the key ring](architecture/08-auth-and-secrets.md)
