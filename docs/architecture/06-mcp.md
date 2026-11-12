# 6. MCP connections

Remote HTTP servers first. The adapter is `Drover.Adapters.Mcp`, built on the official C# SDK 2.4 (Apache-2.0), which targets spec revision 2026-07-28 and falls back to older servers on its own (verified).

## What is built

Built and tested: the `IToolSource` interface and its conformance suite; the adapter with automatic protocol choice, the per-connection override, and both client lifetimes; no-auth and header connections; the catalog with definition pinning; per-principal credential lookup; the taint rule; the result size limit; the connections API at `/api/spaces/{spaceId}/tool-connections`.

Not built yet, and described below as the design: OAuth and everything under it (discovery, the callback bridge, token cache, refresh, `needs_reconnect`); refreshing a catalog on its time-to-live, hourly, or on a server's notification (today it is listed when a connection is added and when an admin presses refresh); saving a result that was cut as a file; the connection's status after a failed call (a failed listing sets it; a failed call is reported to the model and logged); the connections screen.

Details the first build settled:

- **A header credential is one secret.** The header's name and value are stored together in the vault, so the row holds only a secret id and the API never returns either part. A connection may not set a header that belongs to the transport (`Host`, `Content-Length`, `Cookie`, the protocol's own headers, and the like), and an address with a user name or password in it is refused.
- **Limits on what a server can make Drover keep.** At most 500 tools per connection; a description is stored up to 8,000 characters; a tool whose input schema is not an object, or is larger than 64 KB, is not stored. These are constants today and belong in the deployment policy before a shared server relies on them.
- **A server's "read-only" is a claim.** It sets the sensitivity of a tool the first time the tool is seen. It never lowers the sensitivity of a tool already in the catalog, and it is never a reason to run a call again after a restart: only a built-in tool that Drover marks as reading is repeated.
- **Tainted runs rebuild the agent.** The agent framework can stop for approval only on tools that were marked for it when the agent was built. When a run becomes tainted, it is stopped before its next model call and its agent is built again with every consequential `allow` tool marked to ask; the run then goes on from the stored conversation. No model call and no tool call is repeated. A consequential tool called in the same step as the untrusted read, before the rebuild, is refused with a message that it needs approval, and the model can ask again.
- **A session's outbound rule is the one it was opened under.** A kept session of the older protocol generation makes its requests under the space's private-address rule as it was when the session was opened. A changed rule applies from the next session, at most ten idle minutes later.

## Two protocol generations, stateless preferred

MCP has two generations in use. Drover supports both and prefers the newer one.

| Generation | Spec revisions | Shape |
| --- | --- | --- |
| **Stateless** | 2026-07-28 and later | No handshake and no session. Each request stands alone. Capabilities come from a discovery request. |
| **Session-based** | 2025-11-25 and earlier | An `initialize` handshake, usually a session id, and a long-lived stream for notifications. Transport is Streamable HTTP, or the older HTTP with SSE. |

How Drover chooses, per connection (behavior verified in the SDK at 2.4.0):

| Setting | Behavior |
| --- | --- |
| **Automatic** (default) | Try stateless first. If the server does not speak it, fall back to the handshake, and from Streamable HTTP to the older transport. The SDK does this itself when no protocol version is forced. |
| Stateless only | Force the current revision. A server that cannot do it fails with a clear message. For operators who want no session state anywhere. |
| Session-based only | Force a legacy revision. An escape hatch for a server that misbehaves when probed. |

After the first connection Drover stores what was negotiated (protocol version, and whether a session was issued) on the connection and shows it as a small badge: "stateless" or "session-based". The choice is cached per server, as the spec recommends, and re-checked when a connection is edited or fails.

What differs by generation, and what Drover does:

| Concern | Stateless | Session-based |
| --- | --- | --- |
| Client lifetime | Created per use. Cheap: one discovery request, cached. No connection is held between calls. | One client is cached per connection and credential, with idle eviction. Recreated when the server reports the session gone. |
| Tool list changes | List results carry a time-to-live; Drover refreshes the catalog when it lapses. A subscription stream is optional and added later; the SDK has no high-level helper for it yet. | Notifications on the session's stream while the cached client is alive, plus the same hourly refresh |
| A server asking the user a question | Arrives as an "input required" result that the SDK resolves and retries | A server-initiated request on the session |
| Cancel | Closing the response stream | A cancel notification |
| Scaling Drover to several instances | Nothing to share between instances | A session lives in one instance; another instance simply opens its own |

Stateless is preferred because it has no connection to keep alive, nothing to lose on a restart, and nothing that pins a tool server to one Drover instance.

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
