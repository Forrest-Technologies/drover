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
- **Passkeys**: Identity has built-in passkey support on .NET 10 and later (verified). Offered in Phase 2 alongside external sign-in.
- **External sign-in** (Phase 2): ASP.NET Core's OpenID Connect handler, as a client. For the MAUI app the flow runs in the system browser against the Drover server and ends with a one-time code handed back to the app through a registered link.
- **No OAuth server inside Drover** until something needs one. If third-party clients ever need delegated access, OpenIddict (Apache-2.0) is the candidate.
- **Rate limiting** on sign-in and token endpoints through ASP.NET Core's rate limiter. Today that is sign-in, first-run setup, registration, changing a password, and redeeming a reset code, per remote address: 10 attempts a minute, set by `Drover:SignInRateLimit` (`PermitLimit`, `Window`).
- **Request lines are not logged.** The web host's request log is off below warnings, because a request line includes the query string and the callback of a tool server sign-in carries a one-time code there ([06-mcp.md](06-mcp.md)).
- **Lockout**: wrong passwords are counted per account in two buckets, and five in one bucket lock that bucket for five minutes. One bucket is for sign-ins from a browser that has signed in to this account before; the other is for everyone else. Strangers' failures lock only the stranger bucket, so anyone who knows the owner's email can lock out new browsers but not the owner's own; guessing stays capped at five per five minutes in each bucket, ten in all. A right password resets only its own bucket. The stranger bucket is Identity's own counter (`access_failed_count`, `lockout_end`); the other is `users.known_browser_access_failed_count` and `users.known_browser_lockout_end`, with the same limit and lockout time, kept by `AccountService`. A wrong password, an unknown email, and a locked account all answer 401 with the same body, so the answer does not say which emails exist. The password is hashed before the lockout decision, and an unknown email is hashed against a decoy, so the timing does not say either.
- **The browser marker** says which bucket a sign-in counts in, and nothing else. Every successful sign-in, first-account setup, or registration sets `__Host-Drover.Known`: `HttpOnly`, `Secure`, `SameSite=Strict`, path `/`, for a year. Its value is the user id protected with Data Protection (purpose `Drover.KnownBrowser.v1`, time-limited to the year), so it cannot be forged, read for the id, or carried to another account; one that does not unprotect, has run out, or names a different account counts as no marker. It is not a credential: no endpoint but sign-in reads it, and it signs nobody in. Signing out leaves it, since the browser has still signed in before. A browser holds the marker of the last account that signed in on it. Someone who copies the marker gets the known-browser bucket's five guesses, no more.
- **Passwords** need 12 characters and no composition rules.

### How the web cookie works

- The session cookie is `__Host-Drover.Session`: `HttpOnly`, `Secure`, `SameSite=Lax`, path `/`, sliding for 14 days. It carries the user id, email, display name, the id of its row in `user_sessions`, and the security stamp it was issued with. Every request checks that the session row still exists, in a scope with only the user set (`user_sessions` is user-owned), so a session ended on the server is ended for every copy of its cookie at once. Every five minutes the check also compares the stamp with the database and records the session as seen; a changed stamp, or a user who no longer exists, signs the cookie out. The check is `SessionStamp`, the sessions are `SessionService`, and the clock is an injected `TimeProvider`. Opening a session removes that person's sessions left unseen for longer than the cookie lasts. A cookie issued before sessions had rows names none and is refused, so the person signs in again.
- **Signing out** removes the session's row and clears the cookie. Only that session ends; the person's other browsers stay signed in.
- **Changing a password** (`POST /api/account/password`, signed in, with the current password and the new one): a wrong current password answers 400 and counts in the stranger bucket of the lockout, so a stolen session cannot lock the owner's own browser out, and the endpoint shares the sign-in rate limit. On success the stamp changes, every other session of the person is removed, and this session's cookie is issued again with the new stamp, so it stays signed in.
- **A forgotten password**, on a server that cannot send email, is reset by the operator. `--reset-password person@example.com` runs instead of the server (in compose, `docker compose run --rm drover --reset-password person@example.com`), prints a one-time reset code to its log, and exits; it names no other secret, as with the setup code, and does not repeat the email it was given, known or not. Only a SHA-256 hash of the code is stored, in `user_tokens`, with its expiry; a new code replaces the old. The person sends the code with a new password to `POST /api/password-reset`, which shares the sign-in rate limit. The code works once and for 15 minutes; a wrong, used, or expired code and an unknown email all answer 400 with the same body. Redeeming it sets the password, lifts any lockout in both buckets, and ends every session of that person. The code is 12 characters from a 32-letter alphabet (60 bits), so guessing it within its lifetime under the rate limit is not a practical attack.
- Antiforgery follows the cookie-to-header pattern. `GET /api/antiforgery` sets the antiforgery cookie (`__Host-Drover.Antiforgery`, `HttpOnly`, `SameSite=Strict`) and a readable `XSRF-TOKEN` cookie holding the request token. A client sends that value in `X-XSRF-TOKEN` on every `POST`, `PUT`, `PATCH`, and `DELETE`, sign-in and setup included. Without it the request is refused with 400. Sign-in and setup issue a fresh pair for the new user.
- An unauthenticated request to a protected endpoint gets 401 and a refused role gets 403, never a redirect.
- Sign-in cannot use Identity's `SignInManager` or its security-stamp validator as they are: their user lookup runs without a tenant, and row-level security would return nothing. `AccountService` finds the user id through `sys.find_user_for_sign_in`, which returns the id and nothing else, then reads that one row in a transaction with only the user set, and uses `UserManager` for the password check and the lockout counters.

### From request to tenant

1. Authentication yields a user id.
2. The request names a space, in the route for space-scoped endpoints.
3. Middleware confirms the user's membership of that space and loads their role. The lookup runs in a transaction that has only the user id set; the `memberships` policy lets a user read their own rows.
4. The middleware sets the space on `TenantContext` (the user is already set). Everything after runs under row-level security for that space and user. An endpoint filter opens the tenant transaction, commits it when the endpoint succeeds, and rolls it back on a 4xx, a 5xx, or an exception, before the response is written.
5. The role an endpoint asks for (`RequireSpaceRole`) is checked in the same middleware, before the transaction opens.

Not signed in: 401. Not a member, or no such space: 404, the same body for both, and the only thing read is the caller's own memberships. A member without the role: 403.

### First run

The setup screen appears only while the `users` table is empty, checked through `sys.health()`. Creating the owner account, the first space, and the membership is one transaction. After that the setup endpoints return 404 for good.

On a server reachable from the internet, an empty instance could be claimed by whoever finds it first. The server prints a one-time setup code to its log at first start, and the setup screen always asks for it (the `RequireSetupCode` deployment rule, which both presets set; [16-deployment-policies.md](16-deployment-policies.md)). There is no exemption for a request from the same machine: a reverse proxy or an SSH tunnel on the same host makes a remote request look local, so the address proves nothing. The code is not stored: a restart while the instance is still empty prints a new one.

`GET /api/setup` answers 200 while setup is open (and says the code is required), and `POST /api/setup` creates the account and signs the owner in. Two requests at once take turns on a PostgreSQL advisory lock and count users again after getting it, so exactly one account is created.

### Registration

Other people join through `POST /api/register` only where the deployment policy opens it (the `OpenRegistration` rule, off in both presets). The endpoints answer 404 while the policy leaves registration closed and until the first account exists. Where `RequireTermsAcceptance` is on, `GET /api/register` names the version of the terms (`Drover:Policy:TermsVersion`), the request must carry that version, and `users.terms_version` and `users.terms_accepted_at` record what was accepted. Where `RequireEmailVerification` is on, registration answers 501 until the server can send the email. A new account gets a personal space, as the first account does. Registration shares the sign-in rate limit.

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

`Reveal` is called by adapters at the moment of use and nowhere else. No API endpoint calls it: there is no route that returns a secret value. A test asserts that no type in `Drover.Contracts` has a property carrying a secret value, directly or through a type it reaches; the only exceptions are the inbound forms a person fills in with their own password or model key (`SignInRequest`, `SetupRequest`, `RegisterRequest`, `ChangePasswordRequest`, `ResetPasswordRequest`, `CreateModelConnectionRequest`, `UpdateModelKeyRequest`), which print nothing from `ToString`. Responses carry `HasValue` and `LastUsedAt` only: `GET /api/spaces/{spaceId}/secrets`, open to admins and owners, lists that metadata.

Every vault method runs inside a tenant scope the caller opened and commits, and the vault never opens one. `Reveal` throws `KeyNotFoundException` for a secret that does not exist or that row-level security hides, and `CryptographicException` for a ciphertext that does not belong to its row.

### First adapter: Data Protection with rows in PostgreSQL

- Ciphertext lives in the `secrets` table, which is space-owned with an audience.
- Encryption uses ASP.NET Core Data Protection. The protector's purpose string is built from the space id and the secret id: `Drover.Secrets.v1 / {spaceId} / {secretId}`.
- Purpose binding means a ciphertext copied into another space's row, or another secret's row, does not decrypt. Tenant isolation holds for the ciphertext as well as the row.
- Key rotation and algorithm choice are Data Protection's. Old keys are kept so old secrets still decrypt.

Why Data Protection and not hand-written encryption: the library rule. It is the platform's own answer, it handles key rotation, and it leaves no cryptographic code for Drover to get wrong. It is wrapped by `ISecretVault` only because the hosted service will realistically want an external key manager.

### Where the key ring lives

| Deployment | Key ring | Protected by |
| --- | --- | --- |
| Self-hosted compose | Files on the `drover-keys` volume, separate from the database volume. The server reads the path from `DataProtection:KeyPath`, and without it uses `drover/keys` under the user's local application data. The directory is created readable by its owner only, and at every start, on Unix, a key ring that is open to the group or others is tightened to 0700 (files 0600), with a warning in the log if that fails. | File permissions; optionally a certificate the operator supplies |
| Hosted | Database or object storage | A certificate or a cloud key service, through Data Protection's own key-encryption options. The server refuses to start without one (the `KeyRingProtection` rule). |

A certificate is given as PEM files: `DataProtection:CertificatePath`, and `DataProtection:CertificateKeyPath` when the key is in a separate file. With either preset, setting a certificate encrypts the keys at rest. A cloud key service will be its own adapter when the hosted service needs it; today only a certificate satisfies the rule.

A database dump alone does not reveal secrets, because the keys are elsewhere. A backup needs both volumes, and losing the key volume loses every stored secret. The status page and the README say so, and the status page warns when the key ring has never been backed up to a second location the operator has confirmed.

### Never back to a client, never into the log

| Path | Control |
| --- | --- |
| API responses | No contract type carries a value. Enforced by a test. |
| Model context | Adapters attach credentials at the transport. The engine never places a secret in a message. |
| Tool arguments and results | Before a tool call's arguments, output, or result are stored or emitted as events, they pass through a redactor. It replaces exact matches of every secret value the run has revealed, and common token shapes, with `[redacted]`. |
| Logs | Structured logging only, as the guide requires. Secret-bearing types override `ToString` to print `[secret]`, and a log test scans captured output from the integration suite for seeded secret values. |
| Sandbox environment | Secrets a command needs are passed per command, never written to the sandbox's persistent files by Drover |

Built in step 9 for the secrets a run reveals, which today is the model connection's key: the engine's `Redactor` is applied to tool results before they are returned to the runtime, to tool arguments before they are stored in `tool_calls` and in the thread's messages, and to both before they are written to events. The token shapes it knows are provider keys that start `sk-`, GitHub and Slack tokens, AWS access key ids, JSON web tokens, bearer header values, and PEM private key blocks.

The redactor is a seatbelt, not a guarantee: a tool that returns a secret in a transformed form (encoded, split) will not be caught. The primary control is that secrets do not enter tool arguments in the first place.

### Database credentials

| Credential | Held by | Notes |
| --- | --- | --- |
| `drover_owner` password | The migrator step | Used at start to apply migrations, then the connection is closed. If the serving process is given it (`ConnectionStrings:DroverOwner`), the `AllowMigrationCredentialInServer` rule decides whether the server may start. |
| `drover_app` password | The running server | The only credential request handling ever uses |

In the single-container compose deployment both passwords sit on the same secrets volume, because one process migrates and then serves. The separation still does its main job: no code path in the running server holds a connection that can bypass row-level security. On the hosted service, migration is a separate job with its own credential, and the server never sees the owner password.

## Transport and browser hardening

- HTTPS is the operator's reverse proxy in self-hosting; `compose.proxy.yaml` is a ready one ([13-deployment-and-push.md](13-deployment-and-push.md#reaching-drover-from-another-device)). The server trusts forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`, one hop) only from the addresses or networks listed in `Drover:Proxy:TrustedAddresses`. With none listed, none are read, loopback included, so a client cannot claim an address or a scheme by sending a header. The per-address sign-in limit therefore counts the real client behind a trusted proxy and the proxy's own address otherwise. An entry that is neither an address nor a network stops the server at start. So does the host's own switch (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`): it adds a second forwarded-headers step that would trust every address, so the server refuses to start with it on. Only an attempt (sign-in, first-account setup, registration) counts against the sign-in limit; asking whether setup or registration is open does not, so a stranger cannot spend the allowance of everyone behind the same address.
- The session and antiforgery cookies are `Secure` whatever scheme the request came in on, and keep the `__Host-` prefix. The cookie policy stamps `Secure` on every cookie the server sets. A browser keeps such cookies only for a secure origin, which is HTTPS or `http://localhost` (and `127.0.0.1`), so the compose deployment works at `http://localhost:8080` and sign-in fails closed on any other plain-HTTP address. One browser family does not treat `http://localhost` as secure for cookies; there, use the reverse proxy. The antiforgery cookie's own setting is `SameAsRequest` only because the framework refuses to answer a plain-HTTP request under `Always`; over plain HTTP the cookie policy is what makes it `Secure`.
- A content security policy with no inline script and no remote origins by default. `wasm-unsafe-eval` is allowed, as Blazor WebAssembly requires.
- Standard headers: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, frame denial (`X-Frame-Options: DENY` and `frame-ancestors 'none'`), on every response. The policy is `default-src 'self'` with `script-src 'self' 'wasm-unsafe-eval'` and `style-src 'self'`; if the web client needs inline styles, loosen `style-src` there and say why.
- File downloads are served with `Content-Disposition: attachment` and a neutral content type unless the type is on a short image allowlist.
- Outbound requests the server makes on a user's behalf (MCP URLs, OAuth discovery, model base URLs) are checked against private and link-local address ranges on the hosted service, so a tenant cannot aim the server at internal addresses. Self-hosters allow private ranges by default, since local models and LAN servers are normal there; a server that opens registration to strangers must forbid them, or it does not start ([16-deployment-policies.md](16-deployment-policies.md)).
