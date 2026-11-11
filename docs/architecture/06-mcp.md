# 6. MCP connections

Remote HTTP servers first. The adapter is `Drover.Adapters.Mcp`, built on the official C# SDK 2.4 (Apache-2.0), which targets spec revision 2026-07-28 and falls back to older servers on its own (verified).

## Who owns what

| Thing | Owner | Reason |
| --- | --- | --- |
| Connection definition: URL, auth style, name | Space | Admins curate which servers exist |
| Tool catalog for a connection | Space | Grants reference it |
| Grant to use a tool | Bot | Per bot, per tool |
| Credential | Space, or one user | See below |

**Credentials never belong to a bot.** A bot is a set of grants, not an identity. If bots held sign-ins, a child bot could end up with a credential its parent lacks, and "who is this acting as" would have no answer.

Each connection has a `credential_scope`:

| Scope | Meaning | Default for |
| --- | --- | --- |
| `per_user` | Each person signs in themself. A run uses the credential of its principal and nobody else's. | OAuth |
| `space` | One credential shared by everyone in the space | Header or API key, and no-auth |

An admin can set an OAuth connection to `space` on purpose (a shared service account). The UI says plainly that every bot run in the space will act as that account.

### How a bot acting for one user cannot use another's sign-in

1. Every run has a `principal_user_id`, fixed at creation and inherited by child runs.
2. The worker's database transactions carry that user id.
3. `tool_connection_credentials` and `secrets` carry an audience: the database returns a per-user row only inside that user's transactions ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)).
4. The credential lookup has no fallback. If the principal has no credential, the tool call returns "this connection needs you to sign in", and the user sees a connect prompt.

Step 3 means a coding mistake in step 4 still cannot fetch someone else's token.

## The three auth styles

### No auth

Save the URL. Drover lists tools immediately.

### Header or API key

The admin enters a header name and value. The value goes to the vault and is never returned by the API; the UI shows only "set" and a replace button. The adapter adds the header to each request.

### OAuth

What the SDK does (verified in source): protected resource metadata discovery, authorization server discovery through both standard forms, PKCE with S256, resource indicators, client ID metadata documents, dynamic client registration as fallback, pre-registered clients, token refresh, scope step-up, and issuer validation.

What Drover adds:

| Piece | Design |
| --- | --- |
| Client identity | In priority order, following the spec: a client id the admin typed in; else a client ID metadata document that Drover serves at `{public-url}/oauth/client.json`; else dynamic registration; else ask the admin for a client id. The metadata document needs a public HTTPS address, so a server on `localhost` uses dynamic registration. |
| Redirect address | `{public-url}/oauth/mcp/callback` on the Drover server, for every client. The MAUI app opens the system browser; the callback page says "Connected" and offers a link back to the app. |
| Token storage | The SDK's `ITokenCache` has no user or server key. Drover creates one cache instance per credential row. It reads and writes the token set through `ISecretVault`, inside a transaction for the credential's user. Client registration results live in the same token set, as the SDK expects. |
| The browser round trip | See the next section |
| Refresh | The SDK refreshes on demand. Drover also refreshes credentials that expire soon, in the background, so a scheduled run at night does not start with a dead token. |
| Refresh failure | The credential's status becomes `needs_reconnect`. The tool call returns a structured error. The user gets a signal and a push: "Reconnect Calendar". Tools stay visible to the bot; calling them explains what is wrong. |

#### The browser round trip

The SDK's OAuth client expects to call a handler with the authorization address and **await** the code in the same process. There is no "begin now, complete later" API and no way to serialize the pending state (verified at 2.4.0). In a web server, the code arrives in a different HTTP request.

The bridge:

1. `POST /api/tool-connections/{id}/connect` starts a background connect task. The SDK calls Drover's handler with the authorization address.
2. The handler stores an `oauth_flows` row (hash of `state`, user, connection, ten-minute expiry), parks a completion source in memory keyed by `state`, and returns the address to the endpoint, which returns it to the UI.
3. The browser goes to the provider and comes back to `/oauth/mcp/callback?code=...&state=...`.
4. The callback resolves `state` through `sys.resolve_oauth_state`, checks that the signed-in user is the one who started the flow, and completes the parked source. If the source lives in another server instance, the code is passed there with a PostgreSQL `NOTIFY` carrying the state hash, and the receiving instance reads the code from the row.
5. The SDK exchanges the code and writes tokens through the cache. The row is deleted.

Limits, stated honestly:

- A server restart during those ten minutes loses the pending sign-in. The user clicks connect again. Nothing is left half-done.
- The SDK's roadmap names auth as the focus of the 2.x line. If it gains a split API, the bridge shrinks to nothing; the rest of this design stays.

If the bridge proves fragile, the fallback is for Drover to run the interactive leg itself and hand the SDK finished tokens through the cache. That means owning OAuth code, which the library rule argues against, so it is the fallback and not the plan.

## Catalog: listing, caching, refresh

Tools are stored in the `tools` table. A run never lists tools from a server while it assembles; it reads the catalog. That keeps run start fast and gives the bot the same definitions a human reviewed.

| Event | Action |
| --- | --- |
| Connection saved or credential added | List tools, upsert rows |
| Server announces a list change | Relist. The SDK surfaces the notification. |
| Catalog older than one hour when a run starts | Relist in the background; the run uses the current rows |
| Admin presses refresh | Relist now |

Upsert rules:

- **New tool**: row added, state `available`. No bot gets it. A human or a permitted parent bot must grant it.
- **Tool gone**: state `missing`. Grants are kept, so it works again if it returns unchanged.
- **Definition changed** (name, description, input schema, or annotations hash differs): the row updates and every grant on it becomes `needs_review`, which behaves as `ask` until a human looks at the diff and confirms. A server cannot quietly turn an approved harmless tool into something else.

Names shown to the model are `{connection-slug}__{tool-name}`. Two servers cannot shadow each other, and a server cannot impersonate a built-in.

For `per_user` connections, the catalog is the union of tools seen across users' sign-ins. If a server hides a tool from one user, calling it fails with the server's own error.

### When a server is down

- The bot still sees its granted tools, from the catalog.
- A call returns a structured error result: "the Calendar server did not respond". The model can say so or try something else.
- Outbound calls have a timeout, and a circuit breaker stops repeated calls to a dead server for a short while.
- The connection's status and last error show on the connections screen and the status page.

## Local process servers

A stdio server is an arbitrary command. It runs only inside a sandbox, started through `ISandboxProvider`, with its input and output streamed through the sandbox's process channel. The engine sees it as another `IToolSource`. It is never started on the host: there is no host process launcher in the codebase to misuse, and `System.Diagnostics.Process` is listed in `BannedSymbols.txt` for every shipping project. This is Phase 2, with the Docker adapter.

## Tool results are untrusted input

A tool result is text chosen by a third party that the model will read. The design assumes some results are hostile and limits what follows from that.

| Limit | How |
| --- | --- |
| The model cannot grant itself anything | Grants are enforced by the gate at call time. A hostile result can only steer the bot toward tools it already holds. |
| Least privilege is the default | New tools are never auto-granted. The grant screen shows each tool's own description and annotations. |
| Consequential tools ask | Tools have a `sensitivity`: `read`, `write`, or `critical`. It starts from the server's annotations (read-only, destructive) and a human can raise it. `critical` covers bot management, credential changes, and anything a human marks. |
| **Tainted runs escalate** | Each connection has a `trust` level; servers that return open-world content (web, mail, documents from others) are `untrusted`. Once a run has received a result from an untrusted source, the run is marked `tainted` and, for the rest of that run, `allow` on `write` and `critical` tools behaves as `ask`. A child run inherits the mark. This breaks the chain "read hostile text, then act on it unattended". Spaces can turn it off per bot. |
| Results stay in their place | Results go to the model only as tool-role content, never merged into instructions. Tool descriptions from servers are treated the same way. |
| Size limits | Results over a limit are cut for the model, with the full output saved as a file the user can open |
| No secrets in context | Credentials are added at the transport by the adapter. The model never sees them. Known secret values are scrubbed from arguments and results before they are stored or shown ([08-auth-and-secrets.md](08-auth-and-secrets.md)). |
| Server-initiated requests | Sampling is refused. It is deprecated in the current spec. Elicitation (a server asking the user a question) is declined in Phase 0 and shown as an approval prompt later. |
| Output cannot phone home | Rendered markdown is sanitized. Remote images in bot output are not loaded automatically; an image address can carry stolen data in its query string. They show as a placeholder the user can click. |
| Everything is visible | Each call's arguments, result, and outcome are in the activity view. |

What this does not solve: a bot with broad `allow` grants and no taint escalation can still be talked into misuse. The defaults are set so that takes a human choice.
