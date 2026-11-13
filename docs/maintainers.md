# Maintainers: settings for the day the repository goes public

These settings live in GitHub, not in the code. The workflows assume every one of them. Work down the list once, then tick it off on the tracking issue.

## Checklist

1. **Approval for outside contributors.** Settings, Actions, General: require approval for workflow runs from all outside contributors.
2. **Runner group.** Settings, Actions, Runner groups: restrict the self-hosted runner group to the `release` workflow. A mistaken `runs-on` in any other workflow must not reach it.
3. **Tag protection.** Add a rule for `v*` so only maintainers can create a version tag. `release.yml` runs on that tag.
4. **Release environment.** Create the `release` environment and protect it with required reviewers. `release.yml` names it.
5. **Required checks on `main`.** Require the `ci` and `security` checks and a pull request before merge.
6. **Secret scanning with push protection.** Switch both on. Also switch on the dependency graph; the dependency review job fails without it.
7. **Private vulnerability reporting.** Switch it on. `SECURITY.md` tells people to use it.

The reasons are in [tooling](architecture/10-tooling.md#runners).

## Cutting a release

1. `main` is green.
2. Create the tag on a commit of `main`: `git tag v0.1.0` then `git push origin v0.1.0`.
3. `release.yml` checks the tag is on `main`, builds, tests, writes the SBOM and the source archive, attests both, and creates the GitHub release. It publishes no container image.
4. Check the release has a source archive, an SBOM, and attestations.
