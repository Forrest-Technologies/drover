# Drover

A self-hosted platform for running your own crew of AI bots: fully defined by you, on every device you own, with your own keys.

**Status: early.** The design is written, the solution builds, and CI is live. There is nothing to use yet. Follow progress in the [roadmap](ROADMAP.md).

## Quick start

Requires Docker with Compose. No file to edit and no secret to type.

```bash
docker compose up --build
```

Then open <http://localhost:8080>. The first start builds the image from source and creates the database passwords on a volume; `docker compose down` then `up` keeps them. The server listens on localhost only.

## Run what exists without Docker


Requires the .NET 10 SDK.

```bash
dotnet build Drover.slnx -warnaserror
dotnet test --solution Drover.slnx
dotnet run --project src/Drover.Server      # http://localhost:8080
```

## Where things are

| | |
| --- | --- |
| [ROADMAP.md](ROADMAP.md) | Direction, and links to the issues that track each step |
| [docs/architecture](docs/architecture/README.md) | The design, one file per topic |
| [docs/adr](docs/adr/README.md) | Decisions and what would reopen them |
| [docs/ORCHESTRATION.md](docs/ORCHESTRATION.md) | Handoff for whoever coordinates the build: what to dispatch, in what order, and when to stop |
| [AGENTS.md](AGENTS.md) | Rules for anyone, human or coding agent, changing this repository |
| [CODE_STYLE.md](CODE_STYLE.md) | The C# style guide. Binding. |
| [DESIGN.md](DESIGN.md) | The interface direction, and what the interface never does |

## License

Apache-2.0. See [LICENSE](LICENSE).
