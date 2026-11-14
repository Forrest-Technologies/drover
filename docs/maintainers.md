# Maintainers: settings for the day the repository goes public

These settings live in GitHub, not in the code. The workflows assume every one of them. Work down the list once, then tick it off on the tracking issue.

## Checklist

1. **Approval for outside contributors.** Settings, Actions, General: require approval for workflow runs from all outside contributors.
2. **Tag protection. A precondition for the first tag.** Add a ruleset for `v*` so only maintainers can create a version tag. `release.yml` runs on that tag, and its safety depends on this rule. Do not push the first tag before it is in place.
3. **Release environment.** Create the `release` environment and protect it with required reviewers. `release.yml` names it.
4. **Required checks on `main`.** Require the `ci` and `security` checks and a pull request before merge.
5. **Secret scanning with push protection.** Switch both on. Also switch on the dependency graph; the dependency review job fails without it.
6. **Private vulnerability reporting.** Switch it on. `SECURITY.md` tells people to use it.

The reasons are in [tooling](architecture/10-tooling.md#runners).

## What the release job needs

`release.yml` runs on a GitHub-hosted runner (`ubuntu-latest`), like every other workflow. Docker (the test suite starts PostgreSQL containers) and the `gh` CLI are already there, and .NET comes from `setup-dotnet`. There is no self-hosted runner for this repository.

The job is trusted because it runs only for a `v*` tag behind the tag ruleset (checklist item 2) and waits for the `release` environment's required reviewer. Both must be in place before the first tag.

## Cutting a release

1. `main` is green.
2. Create the tag on a commit of `main`: `git tag v0.1.0` then `git push origin v0.1.0`.
3. `release.yml` checks the tag is on `main`, builds, tests, writes the SBOM and the source archive, attests both, and creates the GitHub release. It publishes no container image.
4. Check the release has a source archive, an SBOM, and attestations.
