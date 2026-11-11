# 8. Sign-in and secret handling

## Sign-in

ASP.NET Core Identity, used directly. It is the platform, it is maintained by Microsoft, and it already covers password hashing, lockout, two-factor, and passkeys.

| Client | Mechanism | Storage on the client |
| --- | --- | --- |
| Web | Identity application cookie: `HttpOnly`, `Secure`, `SameSite=Lax`, with antiforgery tokens on state-changing requests | Browser cookie jar |
| MAUI app | Identity's built-in bearer tokens: short-lived access token, long-lived refresh token, both opaque | Platform secure storage through `ISecureStore` |
| Scripts and integrations (Phase 2) | Personal access tokens: random value shown once, stored as a hash, scoped to one space | The caller's choice |

Decisions:

- **Password hashing**: Identity's default hasher. No extra dependency. The alternatives checked were stale or carried an unsuitable license.
- **Passkeys**: Identity has built-in passkey support on .NET 10 (verified). Offered in Phase 2 alongside external sign-in.
- **External sign-in** (Phase 2): ASP.NET Core's OpenID Connect handler, as a client. For the MAUI app the flow runs in the system browser against the Drover server and ends with a one-time code handed back to the app through a registered link.
- **No OAuth server inside Drover** until something needs one. If third-party clients ever need delegated access, OpenIddict (Apache-2.0) is the candidate.
- **Rate limiting** on sign-in and token endpoints through ASP.NET Core's rate limiter.

### From request to tenant

1. Authentication yields a user id.
2. The request names a space, in the route for space-scoped endpoints.
3. Middleware confirms the user's membership of that space and loads their role. The lookup runs in a transaction that has only the user id set; the `memberships` policy lets a user read their own rows.
4. `TenantContext.Enter(spaceId, userId)` is called. Everything after runs under row-level security for that space and user.
5. Endpoint-level authorization checks the role for admin actions.

No space, no membership, or no role: the request ends with 403 or 404 before any tenant data is touched.

### First run

The setup screen appears only while the `users` table is empty, checked through `sys.health()`. Creating the owner account, the first space, and the membership is one transaction. After that the setup endpoints return 404 for good.

On a server reachable from the internet, an empty instance could be claimed by whoever finds it first. The server prints a one-time setup code to its log at first start, and the setup screen asks for it unless the request comes from the same machine.

## Secrets

Secrets are: model provider keys, MCP header values, OAuth token sets and client secrets, sandbox provider keys and per-sandbox tokens, push keys.

### Interface

```csharp
namespace Drover.Domain.Secrets;

public interface ISecretVault
{
    Task<Guid> Store(SecretKind kind, string value, Guid? privateToUserId, CancellationToken cancellationToken);

    Task Replace(Guid secretId, string value, CancellationToken cancellationToken);

    Task<string> Reveal(Guid secretId, CancellationToken cancellationToken);

    Task Delete(Guid secretId, CancellationToken cancellationToken);
}
```

`Reveal` is called by adapters at the moment of use and nowhere else. No API endpoint calls it: there is no route that returns a secret value. A test asserts that no type in `Drover.Contracts` has a property carrying a secret value; responses carry `HasValue` and `LastUsedAt` only.

### First adapter: Data Protection with rows in PostgreSQL

- Ciphertext lives in the `secrets` table, which is space-owned and user-private.
- Encryption uses ASP.NET Core Data Protection. The protector's purpose string is built from the space id and the secret id: `Drover.Secrets.v1 / {spaceId} / {secretId}`.
- Purpose binding means a ciphertext copied into another space's row, or another secret's row, does not decrypt. Tenant isolation holds for the ciphertext as well as the row.
- Key rotation and algorithm choice are Data Protection's. Old keys are kept so old secrets still decrypt.

Why Data Protection and not hand-written encryption: the library rule. It is the platform's own answer, it handles key rotation, and it leaves no cryptographic code for Drover to get wrong. It is wrapped by `ISecretVault` only because the hosted service will realistically want an external key manager.

### Where the key ring lives

| Deployment | Key ring | Protected by |
| --- | --- | --- |
| Self-hosted compose | Files on the `drover-keys` volume, separate from the database volume | File permissions; optionally a certificate the operator supplies |
| Hosted | Database or object storage | A certificate or a cloud key service, through Data Protection's own key-encryption options |

A database dump alone does not reveal secrets, because the keys are elsewhere. A backup needs both volumes, and losing the key volume loses every stored secret. The status page and the README say so, and the status page warns when the key ring has never been backed up to a second location the operator has confirmed.

### Never back to a client, never into the log

| Path | Control |
| --- | --- |
| API responses | No contract type carries a value. Enforced by a test. |
| Model context | Adapters attach credentials at the transport. The engine never places a secret in a message. |
| Tool arguments and results | Before a tool call's arguments, output, or result are stored or emitted as events, they pass through a redactor. It replaces exact matches of every secret value the run has revealed, and common token shapes, with `[redacted]`. |
| Logs | Structured logging only, as the guide requires. Secret-bearing types override `ToString` to print `[secret]`, and a log test scans captured output from the integration suite for seeded secret values. |
| Sandbox environment | Secrets a command needs are passed per command, never written to the sandbox's persistent files by Drover |

The redactor is a seatbelt, not a guarantee: a tool that returns a secret in a transformed form (encoded, split) will not be caught. The primary control is that secrets do not enter tool arguments in the first place.

### Database credentials

| Credential | Held by | Notes |
| --- | --- | --- |
| `drover_owner` password | The migrator step | Used at start to apply migrations, then the connection is closed |
| `drover_app` password | The running server | The only credential request handling ever uses |

In the single-container compose deployment both passwords sit on the same secrets volume, because one process migrates and then serves. The separation still does its main job: no code path in the running server holds a connection that can bypass row-level security. On the hosted service, migration is a separate job with its own credential, and the server never sees the owner password.

## Transport and browser hardening

- HTTPS is the operator's reverse proxy in self-hosting; the compose file documents one. The server trusts forwarded headers only from configured proxy addresses.
- A content security policy with no inline script and no remote origins by default. `wasm-unsafe-eval` is allowed, as Blazor WebAssembly requires.
- Standard headers: `X-Content-Type-Options`, `Referrer-Policy`, frame denial.
- File downloads are served with `Content-Disposition: attachment` and a neutral content type unless the type is on a short image allowlist.
- Outbound requests the server makes on a user's behalf (MCP URLs, OAuth discovery, model base URLs) are checked against private and link-local address ranges on the hosted service, so a tenant cannot aim the server at internal addresses. Self-hosters can allow private ranges, since local models and LAN servers are normal there.
