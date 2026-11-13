[**Apache License 2.0**](LICENSE) · [Quick start](#quick-start) · [Roadmap](ROADMAP.md)

# Drover

**Run your own crew of AI bots.** Host Drover on your own machine, connect a model provider, and talk to your bots from a desktop or phone browser.

![Drover on desktop, streaming a bot reply in a dark conversation view](docs/screenshots/readme/conversation-streaming-dark.png)

Drover is in early development. You can run the browser app from source today. The native Android and Windows apps are on the [roadmap](ROADMAP.md).

## Quick start

Install Docker with Compose, then run:

```sh
git clone https://github.com/Forrest-Technologies/drover.git
cd drover
docker compose up --build
```

The log prints the setup address and a one-time setup code. Open [localhost:8080/setup](http://localhost:8080/setup), enter the code, create your account, and connect a model to start a conversation. Compose creates the database passwords on first startup and keeps them in Docker volumes. The default configuration listens on this machine only.

To upgrade, check out a newer commit and run the same command. Pending database migrations are applied before the server starts. `docker compose down` keeps your data; only `docker compose down --volumes` deletes it.

Back up the `drover-db` and `drover-keys` volumes together. The keys volume holds the key ring that encrypts every stored model key and password; a database backup without it cannot be read. Steps for backup, restore, and moving machines are in the [self-hosting guide](docs/self-hosting.md).

### Connect a model

Setup asks for a model connection. Choose Venice and paste a key made in your Venice account under API keys, or choose Compatible endpoint and give the base address of any server that speaks the chat completions API. A key is optional for the compatible endpoint. Press Test connection; it reports the result in words. The key is stored encrypted on your server and is not shown again. Setup is the only screen that adds a model connection today.

### Connect a tool server

Open Tool connections (`/tools`), choose Add a tool server, and give it a name and the address of its MCP endpoint. Under How Drover proves who is calling, pick Nothing, A header (a header name and value, stored encrypted), or A sign-in. A sign-in needs the public address set; see [self-hosting](docs/self-hosting.md#the-public-address). Then give the server's tools to a bot in the bot editor (`/bots`). A bot never gets a tool it has no grant for.

### Reach it from another device

Point a domain name at the machine, open ports 80 and 443 to it, then start Drover with the HTTPS proxy in front:

```sh
DROVER_DOMAIN=drover.example.org docker compose -f compose.yaml -f compose.proxy.yaml up --build
```

The proxy obtains a certificate for that name and forwards requests to Drover. Drover trusts the client address and scheme the proxy forwards, and nothing else forwards them. Put `DROVER_DOMAIN=...` in a file named `.env` beside `compose.yaml` to keep it between runs. With a proxy of your own instead, set `DROVER_PUBLIC_URL` to the address people open and `Drover__Proxy__TrustedAddresses__0` to the proxy's address; see the [self-hosting guide](docs/self-hosting.md).

### What building it costs

Requirements: Docker with Compose, and room for a build inside 2 GB of memory.

The image is built from source on your machine. The CI compose job builds it inside a 2 GB memory limit on every change and reports these numbers. Measured on a GitHub-hosted runner (4 cores) with an empty build cache, November 2026:

| Measure | Value |
| --- | --- |
| Build time | 78 s |
| Peak memory of the 2 GB builder, page cache included | 1.38 GB, no out-of-memory kill |
| Image size | 319 MB, plus the PostgreSQL image |

## A closer look

<p>
  <img src="docs/screenshots/readme/bot-editor-dark.png" alt="The bot editor: a bot's character, name, instructions, and its model, tools, memory, schedule, sandbox, and team sections" width="540" />
  <img src="docs/screenshots/readme/conversation-phone-dark.png" alt="A Drover conversation in a phone browser, the bot's character beside each reply" width="190" />
</p>

Every bot is a character. Its visor lights up as it works, and it speaks from beside each reply. Make a bot yours in the editor, then choose which tools it may use. The same thread fits a single column on a phone.

<details>
<summary>Build and test with the .NET 11 SDK</summary>

```sh
dotnet build Drover.slnx -warnaserror
dotnet test --solution Drover.slnx
dotnet run --project src/Drover.Server
```

</details>

## Contributing

Start with the [roadmap](ROADMAP.md) and [contributor guidance](AGENTS.md). The [architecture](docs/architecture/README.md), [decision records](docs/adr/README.md), [interface design](DESIGN.md), [C# style guide](CODE_STYLE.md), and [orchestration notes](docs/ORCHESTRATION.md) explain how the project fits together.

![Drover cover art showing three graphite bot heads, one with a lit amber visor](docs/screenshots/drover-crew.webp)

See [LICENSE](LICENSE) and [NOTICE](NOTICE) for license and attribution details.
