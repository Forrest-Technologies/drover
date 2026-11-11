# 15. Model providers

Facts in this section were read from each provider's own documentation and live API on 2026-11-11.

## Shape

A **model connection** is data: a kind, an address, a way to authenticate, and what was learned about its models. `IModelClientProvider` turns a connection into chat and embedding clients for the agent runtime. One adapter project per kind.

| Kind | Auth | Adapter | Phase |
| --- | --- | --- | --- |
| `openai_compatible` | API key, or none for local models | `Drover.Adapters.OpenAICompatible` | 0 |
| `venice` | API key | `Drover.Adapters.Venice` | 0 |
| `anthropic` | API key | `Drover.Adapters.Anthropic` | 1 |
| `openai_signin` | The user's own ChatGPT plan, self-hosted only | `Drover.Adapters.OpenAISignIn` | 1 |
| `openrouter_signin`, `huggingface_signin`, `copilot_signin` | OAuth to the user's own account | One adapter each | 2 |

Every adapter must pass the model provider conformance suite: list models, plain reply, streaming, tool call round trip, structured output where claimed, reasoning text where claimed, a rate-limit response mapped to "retry later", an invalid key mapped to a clear setup message.

Each connection stores a **capability record per model**, in Drover's own shape: context size, maximum output, tool calling, vision, reasoning and its effort levels, structured output, built-in web search, price per million input and output tokens, privacy mode. The bot editor uses it to show only what a model can do, and cost tracking uses its prices. Providers that publish this are read automatically; for a generic compatible endpoint the operator fills in what matters, with sensible defaults.

## Venice, first-party

Venice is supported as a named provider with its own adapter, not as "a compatible endpoint that happens to work". Setup is: choose Venice, paste a key, done.

### What the adapter does

| Area | Design | Source fact |
| --- | --- | --- |
| Connection | Fixed base address. The setup screen asks only for the key. An inference key is all Drover needs; it never asks for an admin key. | Bearer key; two key types exist |
| Model discovery | Reads the public model list and traits. No key needed, so the model picker works before the key is saved. Refreshed daily and on demand. | The list endpoint is keyless and returns capabilities, context sizes, prices, privacy mode, and traits |
| Capability record | Filled from the list: tool calling, vision, reasoning with its effort options, structured output, web search, context and output limits, prices in dollars. | Fields exist per model |
| Defaults | The starter bot uses the provider's own "default" trait. Other traits (reasoning, code, vision, most capable) are offered as one-tap choices. | Traits endpoint |
| Chat | The OpenAI-compatible chat endpoint through `Microsoft.Extensions.AI.OpenAI`. Streaming, tools, parallel tool calls, structured output, images. | "Any OpenAI SDK works" |
| Provider options | Sent with the documented model suffix form, `model:key=value&key=value`, so no custom request body is needed. | Documented suffix syntax |
| **System prompt** | The provider's own system prompt is switched **off** on every request. A Drover bot's instructions are fully the user's; nothing is prepended. | Default is on unless disabled |
| Reasoning | Reasoning text arrives in a non-standard field. The adapter maps it to Drover's `reasoning` message part. For models that return an encrypted placeholder, the part is marked "not available". Effort maps from the bot's provider-neutral `reasoning_effort`. | Field and placeholder documented |
| Built-in web search | A per-bot model setting: off, on, or automatic, with citations. Citations returned by the provider become source links on the message. This is separate from, and can be used alongside, Drover's own `IWebSearch` tool. | Three-state option; citations returned |
| Privacy mode | Each model's mode (private, anonymized, or attested) is shown as a badge in the model picker, in the provider's own terms, with a link to its explanation. A space setting can restrict bots to private or attested models only. | Published per model |
| Balance and limits | Balance headers on responses feed the status page ("Venice balance: ..."). Rate-limit headers drive backoff. A low-balance or limit error becomes a plain message on the run. | Headers documented |
| Cost | Calculated from token usage and the listed prices. Not read from billing, which needs an admin key. | Prices per model in the list |

### One key, several features

Venice also offers the other provider types Drover has interfaces for. The same connection lights them up, each as its own adapter class in the Venice project, each optional:

| Drover interface | Venice capability | Phase |
| --- | --- | --- |
| `IEmbeddingProvider` | Embeddings | 1, with memory |
| `IWebSearch` | Search endpoint (marked experimental by the provider) | 1 |
| `ITextToSpeech`, `ISpeechToText` | Speech and transcription | 2, with voice |
| Image generation tool | Image endpoints | 2 |

So a self-hoster with only a Venice key gets chat, memory, search, and later voice, with nothing else to configure.

### Not used, and why

| Item | Decision |
| --- | --- |
| The community .NET package for Venice | Small, unofficial. The surface Drover needs is the compatible chat endpoint plus a handful of simple calls; the adapter owns them. |
| The provider's client-side encrypted mode | It disables web search and some tool flows and needs client-side key handling. Worth a look in Phase 2 for bots that do not use tools. |
| The provider's own MCP server | It is a local process. Like every local-process server it runs only inside a sandbox, Phase 2. |
| Wallet-based key creation | Out of scope. The user brings a key. |

### To verify in the first sitting

- That the compatible client surfaces the non-standard reasoning field. If it does not, the adapter adds a small response-reading step at the HTTP pipeline. The conformance suite has a test for reasoning text either way.
- That every option the adapter sets is accepted in suffix form, including the system prompt switch.

### Terms to read before the hosted service

Self-hosting with the user's own key raises no issue in the provider's terms. For a hosted service, the terms require that a product built on the API binds its own users to terms at least as protective, and their general license language speaks of personal use. The owner should read the current terms, and likely contact the provider, before offering Venice on the hosted service. This is listed in [12-risks.md](12-risks.md).

## Generic compatible endpoints

One base address and an optional key covers most hosted providers and local model servers. The adapter lists models from the endpoint when it can and otherwise lets the operator type a model name. Capabilities default to "tools and streaming, nothing else claimed" until the operator says more, so a bot is never offered a feature the model lacks.

On the hosted service the base address is checked against private address ranges ([08-auth-and-secrets.md](08-auth-and-secrets.md)). Self-hosters can point at a machine on their own network.

## Signing in with a subscription

Supported only where the provider's terms permit a third-party app to do it.

### ChatGPT plan, for self-hosted servers

OpenAI documents plan usage for open source and locally hosted apps, including an app on the user's own remote machine. A paid or remotely hosted app needs separate admission. Drover supports it for self-hosting and leaves it off on the hosted service until admitted.

| Aspect | Fact | Design |
| --- | --- | --- |
| Client registration | No application or secret. A client id is issued dynamically, bound to the signed-in user. | Nothing for the operator to set up |
| Sign-in flow | Authorization code with PKCE. The redirect must be a fixed loopback address on the machine where the browser runs. No device-code flow. | See below |
| Tokens | Access one hour, refresh thirty days and rotating | Stored in the vault, private to the user. Refresh is serialized per credential so two runs cannot race the rotation. |
| What it can call | Only the Responses endpoint and the model list | The adapter uses the Responses client and lists models from the plan |
| Request rules | Streaming required, no storage, no system-role items, several common parameters must be omitted, tools grouped in a namespace | A request-shaping step in the adapter. Bot instructions travel in the instructions field. Settings the plan does not allow are hidden for these connections. |
| Not available | Hosted tools such as file search and code execution, image generation, audio input | Capability record says so; Drover's own tools are unaffected |
| Limits | The plan's usage window is shared across all apps; the user can cap each app | Limit errors, including one that arrives mid-stream, become a clear message on the run with the reset time |
| Who | Each person signs in with their own plan | Credential scope is always per user. It is never a shared space credential. |

**The loopback redirect** means the sign-in has to finish on the machine with the browser, which is often not the server.

| Where Drover runs | How sign-in works |
| --- | --- |
| Same machine as the browser | The server listens on the loopback address for the callback. The compose file publishes that port to localhost only. |
| A remote server, user has the desktop app | The desktop app runs the loopback step and sends the resulting credential to the user's server over the API. The server owns refreshes from then on. This matches the provider's own guidance for remote machines. |
| A remote server, no desktop app | A small command, `drover connect openai`, does the same from a terminal. |

On a phone-only setup this sign-in is not possible; the UI says so and offers the key option.

### Others

OpenRouter and Hugging Face offer standard OAuth for third-party apps, including hosted ones, and GitHub Copilot does through its SDK. They follow the same pattern as MCP OAuth: per-user credential, tokens in the vault, a reconnect prompt when refresh fails. Phase 2.

Anthropic and Google prohibit third-party use of consumer plans. Those connections are key-only, and the setup screen says why.

Sign-in adapters can each be switched off by a server setting without a release, in case a provider's terms change.
