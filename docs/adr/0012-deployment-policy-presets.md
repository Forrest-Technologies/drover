# 0012. Hosted-service behavior is a deployment policy preset, not a fork

- Status: Accepted
- Date: 2026-11-11
- Source: [16-deployment-policies.md](../architecture/16-deployment-policies.md)

## Context

The owner self-hosts now and will later run Drover as a service. A shared server needs restrictions a personal one does not: no requests to private addresses, no weak sandbox boundary, provider terms, quotas.

## Decision

One typed `DeploymentPolicy` with named rules and two presets, `self_hosted` (default) and `hosted`. The hosted preset sets every rule to its strict value and locks it. Limits only narrow from deployment to space to bot. Feature code asks the policy a specific question and never checks a mode. A matrix test covers every rule under both presets.

## Alternatives considered

- A separate hosted build or private fork. Rejected: two codebases, and the open one would rot.
- Mode checks in feature code. Rejected: impossible to audit and easy to forget.

## Consequences

Every feature that could hurt other tenants or the operator needs a rule and a test row. The hosted service runs the same code anyone can read.

## Reopen when

A hosted requirement that cannot be expressed as a restriction of the open build.
