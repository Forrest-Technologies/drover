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
