# 0024. Providers are compiled-in adapters

- Status: Proposed in its pull request
- Date: 2026-11-26
- Source: [issue 849](https://github.com/Forrest-Technologies/drover/issues/849), [15-model-providers.md](../architecture/15-model-providers.md)

## Context

Drover reaches model providers through adapters behind an interface it owns. More kinds are coming, and a person who
self-hosts will want one Drover does not ship. The question is how a new provider gets into a server: as a project
built into it, or as an assembly the server loads when it starts.

## Decision

- A provider is a `Drover.Adapters.*` project, compiled into the server and added by one registration call,
  `AddModelProvider<TProvider>`, in `AddDroverModels`. The call also puts the provider's named HTTP clients behind the
  checked outbound handler, so no provider is registered without it.
- **Drover does not load provider assemblies at run time.** A loaded assembly runs in the server's process, with the
  server's database role and the key ring in reach: it could read every tenant's data and every stored key, and
  row-level security cannot hold against code that runs as the role it protects. A provider is trusted code, so it
  is built, reviewed, and shipped with the server.
- A self-hoster who wants another provider builds the server with the adapter. `docker compose up --build` already
  builds from source, so that is a project reference and one registration line.
- The server describes each registered kind (`ModelProviderDescriptor`) and lists the kinds it offers at
  `GET /api/model-provider-kinds`, so a client draws its form from the server and not from a list of its own. The
  compatible kind's descriptor also carries its named providers (a reviewed table in the adapter), so the one
  descriptor holds the only addresses a server names for a person's key; no other kind carries one.
- Which registered kinds are offered is the operator's choice, by the `ModelProviders` and
  `AllowedModelProviderKinds` rules of the deployment policy ([ADR 0012](0012-deployment-policy-presets.md)). Two
  providers that claim one kind stop the server at start.

## Alternatives considered

- Load adapters from a folder at start. Lost: it runs unreviewed code with the server's authority, which is the one
  thing tenancy cannot survive. Sandboxing it would mean a process boundary and a protocol, which is a larger design
  than the problem.
- Run each provider out of process behind a protocol. Lost for now: nothing needs it yet, and the model seam is
  an in-process call with streaming that a process boundary would have to reproduce.

## Consequences

Adding a provider is a pull request or a private build, never a drop-in file. A change to this rule is a security
decision and needs a new decision record that supersedes this one.

## Reopen when

Providers can run outside the server's process with no access to its database role or key ring, behind a protocol
Drover owns, and a deployment's operator can still say which of them run. Or a hosted service needs a provider that
cannot be built into the open server and cannot be reviewed with it.
