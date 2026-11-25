# Maintainers: settings for the day the repository goes public

These settings live in GitHub, not in the code. The workflows assume every one of them. Work down the list once, then tick it off on the tracking issue.

## Checklist

1. **Approval for outside contributors.** Settings, Actions, General: require approval for workflow runs from all outside contributors.
2. **Tag protection. A precondition for the first tag.** Add a ruleset for `v*` so only maintainers can create a version tag. `release.yml` runs on stable version tags, and its safety depends on this rule. Do not push the first tag before it is in place.
3. **Release environment.** Create the `release` environment and protect it with required reviewers. `release.yml` names it.
4. **Required checks on `main` and `develop`.** Require the `ci` and `security` checks and a pull request before merge.
5. **Secret scanning with push protection.** Switch both on. Also switch on the dependency graph; the dependency review job fails without it.
6. **Private vulnerability reporting.** Switch it on. `SECURITY.md` tells people to use it.

The reasons are in [tooling](architecture/10-tooling.md#runners).

## What the release job needs

`release.yml` runs on a GitHub-hosted runner (`ubuntu-latest`), like every other workflow. Docker (the test suite starts PostgreSQL containers) and the `gh` CLI are already there, and .NET comes from `setup-dotnet`. There is no self-hosted runner for this repository.

The job is trusted because it runs only for a stable version tag behind the `v*` ruleset (checklist item 2) and waits for the `release` environment's required reviewer. Both must be in place before the first tag.

## Cutting a release

1. `main` is green.
2. Create the tag on a commit of `main`: `git tag v0.1.0` then `git push origin v0.1.0`.
3. `release.yml` checks the tag is on `main`, builds, tests, writes the SBOM and the source archive, attests both, and creates the GitHub release. It publishes no container image.
4. Check the release has a source archive, an SBOM, and attestations.

## Branches and Android releases

- Start ordinary work from `develop` and target `develop` in pull requests. Keep an explicitly documented dependency branch until its prerequisite merges, then retarget to `develop`.
- `develop` is the preview integration branch and publishes Android prereleases. `main` is production and publishes stable Android APK releases. Promote with a reviewed `develop` to `main` pull request.
- Apply the protection ruleset to both branches: retain the existing seven required CI and security checks, require branches to be up to date, require one approval and Code Owner review, dismiss stale approvals, resolve conversations, and block force pushes and deletion. `.github/CODEOWNERS` names `steven-forresttechnologies` and `VelocityLabs`; Brian must accept his Admin invitation before his reviews qualify.
- The separate merge-access ruleset permits organization owners and repository admins to merge. It does not bypass the check or review ruleset. Enable automatic deletion of merged head branches; the two integration branches remain protected from deletion.
- In the existing `android-preview` environment, retain the legacy tag policy `v*-android-preview.*` and allow exactly the `develop` and `main` branches. Keep custom branch policies; never allow all branches. Keep signing secrets in this environment only. The workflow independently rejects unprotected release refs and never signs a pull request.
- The existing CapRover app `drover` at `drover.forresttechnologies.com` tracks `develop`. Its GitHub push webhook remains enabled. This app is the preview deployment; changing its source branch does not create a second production app. A separate production app must track `main` before the hosted service has simultaneous preview and production deployments.
- Android releases use separate `android-v*` tags and never become GitHub's latest release, preserving the desktop updater's release selection. Desktop release signing and the `release` environment remain unchanged.
