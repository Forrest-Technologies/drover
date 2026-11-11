# Drover

A self-hosted platform for running your own crew of AI bots: fully defined by you, on every device you own, with your own keys.

**Status: early.** The design is written, the solution builds, and CI is live. There is nothing to use yet. Follow progress in the [roadmap](ROADMAP.md).

## Run what exists

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
| [AGENTS.md](AGENTS.md) | Rules for anyone, human or coding agent, changing this repository |
| [CODE_STYLE.md](CODE_STYLE.md) | The C# style guide. Binding. |
| [DESIGN.md](DESIGN.md) | The interface direction, and what the interface never does |

## License

Apache-2.0. See [LICENSE](LICENSE).
