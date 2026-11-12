# 10. Tooling: style enforcement, agent guidance, CI, scanning

## What is already in place

| File | State |
| --- | --- |
| [`/CODE_STYLE.md`](../../CODE_STYLE.md) | The owner's guide, unchanged |
| [`/AGENTS.md`](../../AGENTS.md) | Written. About 150 lines, meant to be read in full every time. |
| [`/.editorconfig`](../../.editorconfig) | Written and tested, see below |
| [`/BannedSymbols.txt`](../../BannedSymbols.txt) | Written and tested |
| `.github/workflows/ci.yml`, `security.yml`, `.github/dependabot.yml` | Live since step 1. Linted clean with zizmor (pedantic) and actionlint before the first run. |
| [`drafts/workflows/release.yml`](drafts/workflows/release.yml), [`framework-canary.yml`](drafts/workflows/framework-canary.yml) | Drafts. The release workflow goes live with the first release; the canary with the agent framework step. |

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

Two small Roslyn projects in `tools/`, referenced by every project through `Directory.Build.props`: `Drover.Analyzers` holds the rules and `Drover.Analyzers.CodeFixes` holds the fixes. They are separate because the compiler hosts analyzers and must not load `Microsoft.CodeAnalysis.Workspaces`, which the fixes need (RS1038). `Drover.Analyzers.CodeFixes` is analyzed like every other project. `Drover.Analyzers` cannot be analyzed by itself during its own build, so `AnalyzersObeyTheirOwnRulesTests` runs every rule over the source of both projects. Eight rules:

| Id | Rule | Notes |
| --- | --- | --- |
| DRV0001 | No `Async` suffix | Exempt: overrides, explicit and implicit interface implementations of members declared outside Drover (an assembly named `Drover` or `Drover.*` counts as Drover), and methods with a synchronous twin, meaning a method of the name without the suffix in the same type. An explicit implementation of a Drover interface is reported on the interface member, not on itself |
| DRV0002 | No local `using` directives | Exempt: `GlobalUsings.cs`, and `using X = Y;` aliases. The code fix declines files with conditional directives in the source or target, and imports carrying directives, to preserve preprocessing semantics. |
| DRV0003 | Regions | A type is non-trivial when it has more than one kind of member or more than three members. Interfaces, enums, and positional records are exempt. Checks names against the standard order, allowing descriptively named groups after the public section. A non-trivial type must have every member inside a region. Any type that has regions is checked for order and for the blank lines. |
| DRV0004 | No `+` on strings | Constant-folded concatenations and user-defined operators are exempt. `+=` on a string is reported too. A chain is reported once. |
| DRV0005 | No silent `catch` | A catch block must log (a call on `ILogger` or `LoggerExtensions`, or a `[LoggerMessage]` method), throw, or return a value built from the exception. A `throw` inside a lambda in the catch does not count. |
| DRV0006 | Tenancy shape | An entity exposed as a `DbSet` must implement `ISpaceOwned` or `IUserOwned`, or be listed in `GlobalTables.manifest` |
| DRV0007 | Vendor containment | A vendor namespace (from a list in the analyzer's configuration) may be referenced only by its adapter project |
| DRV0008 | Data access containment | `DroverDbContext`, `DbSet<T>`, and `IQueryable<T>` may be used only in `Drover.Data`, and no public member there may return `IQueryable<T>`, also inside `Task<>` or another generic. Decided by assembly name: `Drover.Data` is the one allowed assembly, and an assembly ending `.Tests` is exempt. A `var` that infers one of the types is reported. |

DRV0006, DRV0007, and DRV0008 are architecture rules, not style. They give an agent a clear message at compile time for the two mistakes that matter most.

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
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <DroverEnableApple Condition="'$(DroverEnableApple)' == ''">false</DroverEnableApple>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>

  <!-- Every project is checked by Drover's own analyzers, except the analyzer project itself. -->
  <ItemGroup Condition="'$(MSBuildProjectName)' != 'Drover.Analyzers'">
    <ProjectReference Include="$(MSBuildThisFileDirectory)tools/Drover.Analyzers/Drover.Analyzers.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />
  </ItemGroup>

  <!-- The code fixes ride along so editors and dotnet format offer them. -->
  <ItemGroup Condition="'$(MSBuildProjectName)' != 'Drover.Analyzers' And '$(MSBuildProjectName)' != 'Drover.Analyzers.CodeFixes'">
    <ProjectReference Include="$(MSBuildThisFileDirectory)tools/Drover.Analyzers.CodeFixes/Drover.Analyzers.CodeFixes.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Notes:

- `ImplicitUsings` is off so that every using is visible in `GlobalUsings.cs`, as the guide says.
- `GenerateDocumentationFile` is on only because IDE0005 needs it to run in the build. Missing-comment warnings are off.
- `NuGetAuditMode` `all` makes a known vulnerability in any package, direct or transitive, a build error.
- No lock files. Central package management with transitive pinning already fixes every version in one file. Lock files would also record packages the SDK adds on its own, whose versions follow the SDK patch level, so a locked restore would fail whenever a machine's SDK differs by a patch. That is friction for every coding session with no gain in safety.
- Test projects add a second banned-symbols file that lifts the `Console` and `Process` bans where a test needs them.

## CI

### Runners

| Situation | Runner | How |
| --- | --- | --- |
| Repository is public, job runs pull request code | GitHub-hosted | Free, and a stranger's code never touches the owner's machines |
| Repository is public, trusted job (release from a protected tag) | Owner's self-hosted runner | Never executes pull request code |

Every workflow uses `runs-on: ubuntu-latest` except `release.yml`.

The repository is public, so two repository settings must be in place **before the workflows go live in Phase 0 step 1**, because they are the controls outside the workflow files:

1. Actions settings: require approval for workflows from all outside contributors.
2. Runner groups: the self-hosted runner group is restricted to the `release` workflow, so even a mistaken `runs-on` in another workflow cannot reach it.

Also: a tag protection rule for `v*`, a protected `release` environment, and branch protection on `main` requiring the `ci` and `security` checks.

Two more settings the checks themselves depend on, found on the first pull request:

| Setting | What breaks without it |
| --- | --- |
| Dependency graph | The dependency review job fails with "not supported on this repository" |
| Secret scanning, with push protection | A branch rule that requires it refuses every merge. A commit pushed before it was switched on is not scanned, so the next push is what clears the rule. |

### Workflows

| Workflow | Triggers | Jobs |
| --- | --- | --- |
| `ci.yml` | Pull request, push to `main` | Restore, build with warnings as errors, format check, all tests including the isolation suite, vulnerable package check, license check. Compose: validate the file, build the image under a 2 GB memory limit, start it, wait for health. Apps: build the Android shell. |
| `security.yml` | Pull request, push to `main`, weekly | CodeQL, dependency review, secret scan, workflow lint, Dockerfile lint |
| `release.yml` | Version tag | Verify the tag is on `main`, build, test, SBOM, source archive, provenance and SBOM attestations, GitHub release. No images. |
| `framework-canary.yml` | Weekly, and on demand | Move the agent framework packages to their newest versions, prerelease included, run the conformance and engine tests, and open an issue if they fail |

Every third-party action is pinned to a full commit SHA with its version in a comment. The SHAs in the drafts were read from each action's repository on 2026-11-11. Every workflow starts from `permissions: {}` and grants per job. Checkouts set `persist-credentials: false`. No workflow uses `pull_request_target`.

The secret scanner and the workflow linter are downloaded as release binaries and verified against checksums written in the workflow file.

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
