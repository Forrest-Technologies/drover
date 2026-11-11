# 16. Deployment policies

Drover is built for self-hosting first. When the owner later runs it as a service for other people, the same build is switched into a stricter mode by policy, not by a fork and not by scattered `if (hosted)` checks.

## The idea

A **deployment policy** is a set of named rules the operator sets when the server starts. Each rule either allows something, forbids it, or caps it. Feature code never asks "am I hosted?". It asks the policy a specific question: "may this server call a private address?", "is this sign-in adapter enabled?".

Two presets ship:

| Preset | For | Character |
| --- | --- | --- |
| `self_hosted` (default) | One person or household on their own machine | Permissive. The operator is trusted and is usually the only user. |
| `hosted` | A service run for strangers | Every rule at its strict value, and **locked**: no setting in the UI, a space, or a bot can loosen it. |

The operator picks a preset with one setting, `DROVER_POLICY`, and may tighten individual rules beyond it. Loosening a rule below the `hosted` preset is not possible while that preset is selected; the server refuses to start and says which rule.

## Limits only narrow

The same principle as the bot tree, applied to configuration:

```
deployment policy   (operator, at start)
   └─ space policy  (space owners and admins)
        └─ bot settings
```

Each level can restrict what the level above allows and can never widen it. A space can forbid untrusted MCP servers that the deployment allows; it cannot enable a sandbox adapter the deployment forbids. The effective value is computed in one place, `PolicyService`, and is what the engine, the API, and the UI all read.

## Rules

Collected from the rest of this document. Each is a typed property on the `DeploymentPolicy` record.

| Rule | `self_hosted` | `hosted` | Why it exists |
| --- | --- | --- | --- |
| Outbound requests to private and link-local addresses (model base URLs, MCP URLs, OAuth discovery) | Allowed | Forbidden | Stops a tenant aiming the server at internal systems. Local models and LAN servers are normal when self-hosting. |
| Sandbox adapters available | All | Only those with a hard isolation boundary; the Docker adapter is off | Plain containers are not a boundary between strangers |
| Local-process (stdio) MCP servers | Inside a sandbox only | Inside a sandbox only | Never on the host, in any mode |
| Subscription sign-in adapters, per provider | On where the provider permits self-hosted use | Off unless the operator has that provider's approval for hosted use, recorded per provider | Provider terms differ for hosted apps |
| Model providers available | All | A list the operator sets, after reading each provider's terms for hosted use | Same |
| Open registration (`OpenRegistration`) | Off after the first account | Off; the operator may turn it on, and then email verification and acceptance of terms are required | Abuse control |
| Acceptance of terms of service and privacy notice at sign-up | Off | Required, with the version accepted recorded | Legal, and some providers require their terms to flow down to end users |
| First-run setup code (`RequireSetupCode`) | Required for every request | Required for every request; provisioning is the operator's, so the screen is rarely used | Prevents claiming an empty server. No address proves a request is local, because a proxy or tunnel on the same host makes a remote request look local. |
| Per-space quotas: bots, runs per day, concurrent runs, storage, sandbox minutes | Unlimited by default | Set by plan | Fair use and cost |
| Tainted-run escalation ([06-mcp.md](06-mcp.md)) | On, a space may turn it off per bot | On, cannot be turned off | Limits what a hostile tool result can cause |
| Remote images in bot output load automatically | Off, a space may turn it on | Off, cannot be turned on | Data can leak through an image address |
| Key ring protection | File permissions | A certificate or key service is required; the server refuses to start without one | A database dump must not be enough to read secrets |
| Migration credential in the serving process | Allowed (single container) | Forbidden; migration is a separate job | Least privilege |
| Audit log retention and export | Operator's choice | Minimum retention enforced | Accountability |
| Data export and account deletion endpoints | Available | Available and cannot be disabled | Users' rights |
| Telemetry export | Off unless an endpoint is set | Operator's endpoint; message content never included in any mode | Operations |
| Operator access to tenant content | Not applicable | None through the product. Support access, if ever added, is an explicit, audited, time-limited grant from the space owner | Trust |

A new feature that could harm other tenants, cost the operator money without limit, or break a provider's terms on a shared server ships with a rule here.

### Rule names and enforcement today

Each rule is a property of `DeploymentPolicy` with the name below. A rule marked "enforced" is checked at the point of action now; a rule marked "later" has its value and appears on the status listing, and the work named will enforce it.

| Rule | `self_hosted` | `hosted` | Enforced |
| --- | --- | --- | --- |
| `AllowPrivateAddresses` | `true` | `false` | Enforced: every outbound connection |
| `SandboxAdapters` | `All` | `HardBoundaryOnly` | Later: sandbox adapter registration (Phase 1) |
| `AllowHostStdioMcp` | `false` | `false` | Later: MCP tool servers (Phase 1) |
| `SubscriptionSignIn` | `WhereProviderPermits` | `OnlyWithOperatorApproval` | Later: model providers |
| `ModelProviders` | `All` | `OperatorList` | Later: model providers |
| `OpenRegistration` | `false` | `false` (operator may set `true`) | Enforced: `POST /api/register` |
| `RequireEmailVerification` | `false` | `true` | Enforced: `POST /api/register` answers 501 until the server can send email |
| `RequireTermsAcceptance` | `false` | `true` | Enforced: `POST /api/register`, and the version is recorded on the user |
| `RequireSetupCode` | `true` | `true` | Enforced: `POST /api/setup` |
| `SpaceQuotas` | `Unlimited` | `SetByOperator` | Later: quotas |
| `SpaceMayDisableTaintedRunEscalation` | `true` | `false` | Later: tainted-run escalation (Phase 1) |
| `SpaceMayAutoLoadRemoteImages` | `true` | `false` | Later: chat rendering |
| `KeyRingProtection` | `FilePermissions` | `CertificateOrKeyService` | Enforced: server start |
| `AllowMigrationCredentialInServer` | `true` | `false` | Enforced: server start |
| `AuditRetention` | `OperatorChoice` | `MinimumEnforced` | Later: audit retention |
| `AllowDisablingDataRights` | `true` | `false` | Later: export and deletion endpoints |
| `AllowTelemetryContent` | `false` | `false` | Later: telemetry export |
| `AllowOperatorContentAccess` | `false` | `false` | Later: support access, if it is ever added |

`OpenRegistration` is the one rule the operator of a hosted service may loosen: the table's "operator's choice". Every other rule is locked to its preset. The sign-in rate limit, the key ring path, and quota amounts are configuration, not rules.

## Mechanics

| Piece | Design |
| --- | --- |
| Type | `DeploymentPolicy`, a record in `Drover.Domain` with one typed property per rule. No string keys in feature code. `DeploymentPolicy.Rules` is the table of rules (name, value, how loose it is, where it is enforced). |
| Presets | Two static values, `DeploymentPolicy.SelfHosted` and `DeploymentPolicy.Hosted`, the second built from the first with extension methods in the guide's builder style (`AsHosted`) |
| Loading | `DeploymentPolicySetup` in `Drover.Server` builds `DeploymentPolicyOptions` through the platform's options system. `DROVER_POLICY` (or `Drover:Policy:Preset`) names the preset, `Drover:Policy:Rules:<Rule>` tightens one rule, and `Drover:Policy:TermsVersion` names the terms people accept. It is validated when the host starts: a rule set looser than the preset, a key that names no rule, an unknown preset, an enum rule whose number is not one of its values, a hosted server without a key ring certificate, a migration credential (`ConnectionStrings:DroverOwner`) in a server that forbids it, or open registration that asks for terms with no version stops the server. Where the migration credential is forbidden, the server also asks the database who the request connection is at start (`DatabaseRoleGuard`) and refuses unless it is `drover_app` with no superuser or row-level-security bypass. Each requirement is checked when the preset asks for the strict value, whatever the configured value is. Each refusal names the rule. |
| Reading | `PolicyService` in `Drover.Engine` returns the effective value for a space: the deployment rule narrowed by the space's settings. It is the only place that computes one. |
| Space policy | Stored in `spaces.settings` (`SpaceSettings`), restricted to rules a space may narrow: `AllowPrivateAddresses`, `TaintedRunEscalation`, `AutoLoadRemoteImages`. A space can forbid private addresses; it cannot allow them where the deployment forbids. It can turn off tainted-run escalation or turn on remote images only where the deployment rule says a space may. Bot settings do not narrow policy yet. |
| Enforcement | At the point of action, on the server: `OutboundHttp` (below), `POST /api/setup`, `POST /api/register`, and server start. Adapter registration and the tool gate join when those features exist. Never only in the UI. |
| Outbound requests | `OutboundHttp.CreateHandler` is the primary handler of every `IHttpClientFactory` client (set once with `ConfigureHttpClientDefaults`). A request may reach a private address only when the deployment allows it and the request says so: the caller puts the space's effective value on the request with `ForSpace`, and a request that says nothing is treated as forbidden. Forbidden requests resolve the host, are refused if any returned address is private, link-local, loopback, shared, IPv4-mapped, or a NAT64/6to4 form of one, and connect to the address they checked, so the answer cannot change between check and connect. The check runs on every new connection, so a redirect to another host is checked again. Allowed and forbidden requests use separate connection pools. HTTP/3 is refused, since it skips the connection check, and a request is not left free to upgrade to it. A guarded request does not use an outbound proxy. `BannedSymbols.txt` bans `new HttpClient(...)`, `SocketsHttpHandler` and `HttpClientHandler` construction; only the policy code, the browser client, and the handler's own tests are exempt (in `.editorconfig`). |
| Visibility | `GET /api/status` (signed-in people) lists the active preset and every rule with its setting and where it is enforced. The UI hides or explains what a rule forbids: "Not available on this server." |
| Tests | `PolicyMatrix` in `Drover.Server.Tests` has a row per rule. A test fails when a property of `DeploymentPolicy` has no row, and the matrix tests start the server under each preset and check the status listing and each enforced rule. `PolicyServiceTests` in `Drover.Engine.Tests` covers narrowing, including that a space cannot loosen. A source scan in `Drover.Domain.Tests` fails on a source file outside the policy types that names or compares a preset. |

`hosted` is a preset, not a separate product. Everything it does is in the open source repository and a self-hoster running a server for their community can select it too.

## What this is not

- **Not billing.** Plans and payment for the hosted service are outside the open source server. They set quota values through the same space policy any operator can set.
- **Not a feature flag system.** Rules exist to restrict for safety, cost, or compliance. A feature that is simply unfinished is not shipped.
- **Not a replacement for row-level security.** Tenant isolation is on in every mode and has no rule to turn it off.

## Phase

Phase 0 builds the type, the two presets, loading, the status listing, and the matrix test, with the rules that already have something to guard: outbound address checks, the setup code, registration and acceptance of terms, the migration credential, and key ring protection. Each later feature adds its own rule when it is built.

Two things are not done yet: the server cannot send the email that email verification needs, so `POST /api/register` answers 501 where verification is required; and it does not read forwarded headers, so per-address rate limits and the "same machine" check see the proxy's address.
