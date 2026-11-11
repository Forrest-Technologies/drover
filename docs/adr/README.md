# Architecture decision records

One short file per decision that would be expensive to reverse or that an agent would otherwise re-litigate. The design itself lives in [`docs/architecture`](../architecture/README.md); an ADR records that a choice was made, what the alternatives were, and what would reopen it.

## Rules

- File name: `NNNN-short-title.md`, numbered in order. Copy [`0000-template.md`](0000-template.md).
- Status is `Accepted` or `Superseded by NNNN`. There is no draft status: the pull request is the proposal, and merging it is the decision.
- An ADR is never edited to change its decision. Write a new one and mark the old one superseded.
- Keep it under a page. Link to the architecture section for detail.
- Implementation progress does not belong here. That is what issues are for.
- A pull request that goes against an accepted ADR must add the ADR that supersedes it.

## Index

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-build-on-agent-framework-in-one-project.md) | Build on Microsoft Agent Framework fully, inside one project | Accepted |
| [0002](0002-isolation-in-postgresql.md) | Isolate users and groups with PostgreSQL row-level security | Accepted |
| [0003](0003-one-data-path-for-all-clients.md) | One data path for every client: JSON API and Server-Sent Events | Accepted |
| [0004](0004-runs-are-durable-jobs.md) | A run is a durable job with a gapless event log | Accepted |
| [0005](0005-grants-narrow-down-the-tree.md) | Tool grants are per bot and per tool, narrow down the bot tree, and are enforced outside the model | Accepted |
| [0006](0006-credentials-belong-to-users-or-spaces.md) | Tool and model credentials belong to a user or a space, never to a bot | Accepted |
| [0007](0007-mcp-both-generations-stateless-preferred.md) | Support both MCP protocol generations and prefer stateless | Accepted |
| [0008](0008-own-ui-components.md) | Drover's own UI components on plain CSS tokens | Accepted |
| [0009](0009-build-from-source-no-images.md) | Deploy with compose files that build from source; publish no images | Accepted |
| [0010](0010-issues-are-the-source-of-truth.md) | GitHub Issues are the source of truth for work | Accepted |
| [0011](0011-repositories-and-services.md) | Repositories store and retrieve; services hold business logic | Accepted |
| [0012](0012-deployment-policy-presets.md) | Hosted-service behavior is a deployment policy preset, not a fork | Accepted |
