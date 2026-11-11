# 0009. Deploy with compose files that build from source; publish no images

- Status: Accepted
- Date: 2026-11-11
- Source: [13-deployment-and-push.md](../architecture/13-deployment-and-push.md)

## Context

Owner requirement. Ease of standing up is a product feature.

## Decision

`docker compose up` builds the server image locally from a tagged release. Secrets are generated on first start. A release is a tag, a source archive, an SBOM, and attestations. The image must build within a 2 GB memory budget, enforced in CI.

## Alternatives considered

- Publishing images. Ruled out by the owner.

## Consequences

First start takes a build. The build budget is a standing constraint on what goes into the image.

## Reopen when

The build cost measurably hurts adoption; report with numbers and consider a prebuilt web client as a verified release asset.
