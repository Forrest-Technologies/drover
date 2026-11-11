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
| Open registration | Off after the first account | Operator's choice, with email verification required | Abuse control |
| Acceptance of terms of service and privacy notice at sign-up | Off | Required, with the version accepted recorded | Legal, and some providers require their terms to flow down to end users |
| First-run setup code | Required for remote setup | Not applicable; provisioning is the operator's | Prevents claiming an empty server |
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

## Mechanics

| Piece | Design |
| --- | --- |
| Type | `DeploymentPolicy`, a record in `Drover.Domain` with one typed property per rule. No string keys in feature code. |
| Presets | Two static values, `DeploymentPolicy.SelfHosted` and `DeploymentPolicy.Hosted`, built with extension methods in the guide's builder style |
| Loading | Bound from configuration at start through the platform's options system, validated once. An invalid or loosened hosted policy stops the server with a message naming the rule. |
| Reading | `PolicyService` in `Drover.Engine` returns the effective value for a space or bot: the deployment rule narrowed by the space's policy |
| Space policy | Stored in `spaces.settings` as a record of the same shape, restricted to rules a space may narrow |
| Enforcement | At the point of action, on the server: the outbound HTTP handler, adapter registration, the tool gate, the registration endpoint. Never only in the UI. |
| Visibility | The status page lists the active preset and every rule with its value. The UI hides or explains what a rule forbids: "Not available on this server." |
| Tests | A matrix test starts the server under each preset and asserts each rule's behavior, including that a space setting cannot loosen it. Adding a property to `DeploymentPolicy` without a row in the matrix fails the build. |

`hosted` is a preset, not a separate product. Everything it does is in the open source repository and a self-hoster running a server for their community can select it too.

## What this is not

- **Not billing.** Plans and payment for the hosted service are outside the open source server. They set quota values through the same space policy any operator can set.
- **Not a feature flag system.** Rules exist to restrict for safety, cost, or compliance. A feature that is simply unfinished is not shipped.
- **Not a replacement for row-level security.** Tenant isolation is on in every mode and has no rule to turn it off.

## Phase

Phase 0 builds the type, the two presets, loading, the status listing, and the matrix test, with the rules that already have something to guard: outbound address checks, the setup code, and registration. Each later feature adds its own rule when it is built.
