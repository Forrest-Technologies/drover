# 10. Tooling: style enforcement, agent guidance, CI, scanning

## What is already in place

| File | State |
| --- | --- |
| [`/CODE_STYLE.md`](../../CODE_STYLE.md) | The owner's guide, unchanged |
| [`/AGENTS.md`](../../AGENTS.md) | Written. About 150 lines, meant to be read in full every time. |
| [`/.editorconfig`](../../.editorconfig) | Written and tested, see below |
| [`/BannedSymbols.txt`](../../BannedSymbols.txt) | Written and tested |
| [`drafts/workflows/ci.yml`](drafts/workflows/ci.yml), [`security.yml`](drafts/workflows/security.yml), [`release.yml`](drafts/workflows/release.yml), [`dependabot.yml`](drafts/workflows/dependabot.yml) | Drafts. They move to `.github/` in Phase 0 step 1. They are not live yet because there is no solution for them to build. |

## Style enforcement

The guide is the authority. The tools are configured to match it. Where no stock tool covers a rule, Drover has its own analyzer; the guide is never bent to fit a tool.

### Tested

`.editorconfig` and `BannedSymbols.txt` were run on SDK 10.0.115 against two files: one written to the guide, one breaking it on purpose.

| Result | Detail |
| --- | --- |
| Compliant file | Builds with zero warnings under `-warnaserror`. `dotnet format --verify-no-changes` exits 0. It uses regions, a primary constructor, a bare `camelCase` field, a `PascalCase` constant, no `Async` suffix, and two blank lines between top-level types. |
| Non-compliant file | Fails the build on every rule listed as "stock" below |

### Coverage map

| Rule in `CODE_STYLE.md` | Enforced by | Kind |
| --- | --- | --- |
| File-scoped namespaces | IDE0161 | Stock, error |
| `var` for all locals | IDE0007 | Stock, error |
| Braces on every block | IDE0011 | Stock, error |
| Allman braces, 4 spaces, final newline, no trailing whitespace | IDE0055 and editor settings | Stock, error |
| Collection expressions | IDE0028, IDE0300 to IDE0305 | Stock, error |
| Target-typed `new` | IDE0090 | Stock, error |
| Primary constructors | IDE0290 | Stock, warning (so, an error in CI). Suppress with a reason when a traditional constructor is justified. |
| Pattern matching, null operators | IDE0041, IDE0074, IDE0083, related style options | Stock, error |
| Private fields bare `camelCase`, **no underscore** | IDE1006 with a naming rule. Observed message: "Prefix '_' is not expected". | Stock, error |
| Constants, types, members `PascalCase`; interfaces `I`; type parameters `T` | IDE1006 naming rules | Stock, error |
| No `.Result`, `.Wait()` | CA1849, plus banned symbols | Stock, error |
| No `.ContinueWith` | Banned symbols (RS0030) | Stock, error |
| No `string.Format` | Banned symbols | Stock, error |
| No `Console.WriteLine` | Banned symbols | Stock, error |
| Structured logging, no interpolation in log calls | CA2254, CA2017 | Stock, error |
| Rethrow with `throw;` | CA2200 | Stock, error |
| Unused usings | IDE0005 | Stock, error |
| Nullable enabled, zero nullable warnings | `<Nullable>enable</Nullable>`, warnings as errors | Compiler |
| **No `Async` suffix unless a sync twin exists** | `DRV0001` | Drover analyzer |
| **Global usings only** | `DRV0002` (alias directives that resolve an ambiguity are allowed) | Drover analyzer |
| **Regions: present, standard order, blank line inside each tag** | `DRV0003` | Drover analyzer |
| **No `+` string concatenation** | `DRV0004` | Drover analyzer |
| **No empty or swallowing `catch`** | `DRV0005` | Drover analyzer |
| Max line length 120 | `max_line_length` in `.editorconfig`. The formatter does not wrap long lines, so review catches the rare miss. | Editor only |
| Method names describe what, cognitive complexity, extension builders over factories | Not mechanically checkable | `AGENTS.md` and review |

Stock rules that contradict the guide or the platform are switched off in `.editorconfig`, each with a comment: the rule that demands the `Async` suffix, `ConfigureAwait` advice, the `LoggerMessage` delegate advice (the guide calls `logger.LogX` directly), and argument null-check advice that nullable reference types already cover.

### `Drover.Analyzers`

A small Roslyn analyzer project in `tools/`, referenced by every project through `Directory.Build.props`. Seven rules:

| Id | Rule | Notes |
| --- | --- | --- |
| DRV0001 | No `Async` suffix | Exempt: overrides, explicit and implicit interface implementations of members declared outside Drover, and methods with a synchronous twin in the same type |
| DRV0002 | No local `using` directives | Exempt: `GlobalUsings.cs`, and `using X = Y;` aliases |
| DRV0003 | Regions | A type is non-trivial when it has more than one kind of member or more than three members. Interfaces, enums, and positional records are exempt. Checks names against the standard order, allowing descriptively named groups after the public section. |
| DRV0004 | No `+` on strings | Constant-folded concatenations of literals are exempt |
| DRV0005 | No silent `catch` | A catch block must log, rethrow, or return a value built from the exception |
| DRV0006 | Tenancy shape | An entity exposed as a `DbSet` must implement `ISpaceOwned` or `IUserOwned`, or be listed in `GlobalTables.manifest` |
| DRV0007 | Vendor containment | A vendor namespace (from a list in the analyzer's configuration) may be referenced only by its adapter project |

DRV0006 and DRV0007 are architecture rules, not style. They give an agent a clear message at compile time for the two mistakes that matter most.

Each rule has tests with compliant and non-compliant samples, and a code fix where one is mechanical (DRV0001, DRV0002, DRV0004).

### `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <DroverEnableApple Condition="'$(DroverEnableApple)' == ''">false</DroverEnableApple>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <ProjectReference Include="$(MSBuildThisFileDirectory)tools/Drover.Analyzers/Drover.Analyzers.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false"
                      Condition="'$(MSBuildProjectName)' != 'Drover.Analyzers'" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

Notes:

- `ImplicitUsings` is off so that every using is visible in `GlobalUsings.cs`, as the guide says.
- `GenerateDocumentationFile` is on only because IDE0005 needs it to run in the build. Missing-comment warnings are off.
- `NuGetAuditMode` `all` makes a known vulnerability in any package, direct or transitive, a build error.
- Test projects add a second banned-symbols file that lifts the `Console` and `Process` bans where a test needs them.

## CI

### Runners

| Situation | Runner | How |
| --- | --- | --- |
| Repository is private | Owner's self-hosted runners for everything | Hosted minutes cost money for private repositories |
| Repository is public, job runs pull request code | GitHub-hosted | Free, and a stranger's code never touches the owner's machines |
| Repository is public, trusted job (release from a protected tag) | Owner's self-hosted runner | Never executes pull request code |

The workflows pick the runner from `github.event.repository.private`, so nobody has to remember to flip a setting on the day the repository goes public:

```yaml
runs-on: ${{ github.event.repository.private && fromJSON('["self-hosted","linux","x64"]') || 'ubuntu-latest' }}
```

Before going public, two repository settings must be set, because they are the controls outside the workflow files:

1. Actions settings: require approval for workflows from all outside contributors.
2. Runner groups: the self-hosted runner group is restricted to the `release` workflow, so even a mistaken `runs-on` in another workflow cannot reach it.

Also: a tag protection rule for `v*`, a protected `release` environment, and branch protection on `main` requiring the `ci` and `security` checks.

### Workflows

| Workflow | Triggers | Jobs |
| --- | --- | --- |
| `ci.yml` | Pull request, push to `main` | Restore in locked mode, build with warnings as errors, format check, all tests including the isolation suite, vulnerable package check, license check. Compose: validate the file, build the image under a 2 GB memory limit, start it, wait for health. Apps: build the Android shell. |
| `security.yml` | Pull request, push to `main`, weekly | CodeQL, dependency review, secret scan, workflow lint, Dockerfile lint |
| `release.yml` | Version tag | Verify the tag is on `main`, build, test, SBOM, source archive, provenance and SBOM attestations, GitHub release. No images. |
| `framework-canary.yml` | Weekly, and on demand | Move the agent framework packages to their newest versions, prerelease included, run the conformance and engine tests, and open an issue if they fail |

Every third-party action is pinned to a full commit SHA with its version in a comment. The SHAs in the drafts were read from each action's repository on 2026-11-11. Every workflow starts from `permissions: {}` and grants per job. Checkouts set `persist-credentials: false`. No workflow uses `pull_request_target`.

Two values are left as repository variables for step 1 to fill, because they must be copied from the upstream release at that moment: the checksum of the secret scanner's binary and the image digest of the workflow linter.

## Scanners

One tool per area. All are free for a public repository and either open source or built into the platform.

| Area | Tool | License | Why this one |
| --- | --- | --- | --- |
| C# static analysis | .NET analyzers, `Drover.Analyzers`, banned symbols, in the build | MIT | Runs on every build, locally and in CI, with the same result |
| C# security analysis | CodeQL, `security-extended` queries | Free on public repositories | Data-flow analysis the build analyzers do not do. Supports `.slnx`. Also scans the workflow files. |
| Dependency vulnerabilities | NuGet audit in the build, mode `all`, with lock files | Built in | Fails the build on a vulnerable direct or transitive package |
| New dependencies in a pull request | Dependency review action | MIT, GitHub | Blocks a pull request that adds a vulnerable or disallowed-license package |
| Dependency updates | Dependabot | GitHub | NuGet, actions, and the Docker base image |
| Secrets | GitHub push protection, plus the gitleaks command-line tool over history | MIT | Push protection stops a secret before it lands. The CLI covers history. The CLI is run directly because its official action requires a license key for organization accounts. |
| Dockerfile | hadolint | GPL-3.0, used as a tool only, nothing linked or shipped | The standard Dockerfile linter |
| Compose file | `docker compose config`, plus `ComposeFileTests` | n/a | See below |
| Workflow files | zizmor and actionlint | MIT, MIT | zizmor finds security problems (unpinned actions, injection, excess permissions). actionlint finds mistakes. They do not overlap. |
| License compliance | nuget-license, with an allowlist | Apache-2.0 | The maintained .NET tool. Checks direct and transitive packages. |
| SBOM | CycloneDX .NET tool, at release | Apache-2.0 | Standard format, reads the solution directly |
| Provenance | GitHub artifact attestations | GitHub | Ties the release archive and SBOM to the workflow run that made them |

### Decisions worth noting

- **No third-party container or compose scanner.** The two widely used ones had their GitHub actions compromised in 2026 (one in March, the other in March and again in April). Drover publishes no images, so image vulnerability scanning has little to scan: the runtime image is Microsoft's base image plus Drover's own build output, and Dependabot tracks the base image. For the compose file, a short test in `Drover.Server.Tests` parses `compose.yaml` and asserts the hardening rules: no privileged mode, no host network, no Docker socket, no host path mounts, read-only root filesystem, capabilities dropped, ports bound to localhost by default. That covers the gap with no new supply-chain exposure.
- **The gitleaks project is in maintenance mode** (security fixes only), and its author has started a successor. The CLI is feature-complete for this use. Revisit when the successor ships an official action.
- **License tool and central package management.** The tool's documentation does not mention central package management. It reads resolved packages after restore, so it should work; Phase 0 step 1 confirms it, and `dependency-review` is the backstop for new packages either way.
- **Scorecard and runtime hardening of runners** were considered and left out for now. They add signal later, once the repository is public and has contributors.
- **Dependabot and `global.json`.** There are reports of project discovery failing when `global.json` pins an SDK the updater does not have. Pin a feature band with `rollForward: latestFeature`, not an exact patch.
