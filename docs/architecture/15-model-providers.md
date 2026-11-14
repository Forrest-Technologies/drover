# 15. Model providers

Facts in this section were read from each provider's own documentation and live API on 2026-11-11.

## Shape

A **model connection** is data: a kind, an address, a way to authenticate, and what was learned about its models. `IModelClientProvider` turns a `ModelEndpoint` into a Drover-owned `IModelChatClient` and lists/tests the connection. `ModelConnection` remains the space-owned persistence entity, with no credentials in either shape. Embeddings remain future work. One adapter project per kind.

Both providers are wired into the server by `AddDroverModels` (`Drover.Server`), which registers the compatible transport before Venice and bridges returned clients with `ModelChatClientBridge`. Every provider call uses a named HTTP client from `IHttpClientFactory`, so the checked outbound handler of the deployment policy applies to model connections. An adapter builds its own requests and knows nothing of policy, so the caller wraps the adapter call in `OutboundAttempt.Begin(effectivePolicy)` and `SpaceOutboundHandler` puts the space's `AllowPrivateAddresses` value on each request made inside it; a request made outside any attempt says nothing and is treated as forbidden. When the handler refuses an address, the adapter maps the refusal to a rejected failure that carries its own message, so the connection test says why instead of "could not be reached"; a refusal is one attempt, never retried and never counted toward the circuit breaker. No tenant transaction is held across these provider calls.

## Connections in a space

A connection is created and managed under `/api/spaces/{spaceId}/model-connections`. Antiforgery applies to every state-changing request.

| Route | Who | What |
| --- | --- | --- |
| `GET /` | member | The space's connections. A connection says `hasKey`; the key is never returned. |
| `POST /` | admin | Create. `kind` is a registered provider kind. A fixed-address kind (Venice) takes no address; a compatible endpoint needs an `http`/`https` address with no credentials, query, or fragment. The key is optional and goes to `ISecretVault` as a `model_key`. |
| `PUT /{id}/key` | admin | Store a new key as a new secret and delete the old one, then return the connection to `pending`. |
| `POST /{id}/test` | admin | Ask the provider whether the connection works. Records `ready` or `failed`, the time, and any balances. A rate-limit answer leaves the status alone. |
| `GET /{id}/models` | member | The stored catalog (`model_catalog`), in Drover's own shape. |
| `POST /{id}/models/refresh` | admin | Discover the provider's models and upsert the catalog. A model that is no longer listed keeps its row, marked `missing`. An empty list marks nothing missing. If the provider cannot answer, the stored catalog is returned with the reason. |

Creating a connection to a literal private address is refused when the space or the deployment forbids private addresses; a host name is checked each time a connection is made, because what it resolves to can change.

Model discovery is bounded. Adapters read a model list with headers first and stop at `MaxModelListBytes`; `ModelConnectionService` refuses a list with more than `MaxModelsPerConnection` models. Both are deployment policy rules ([16-deployment-policies.md](16-deployment-policies.md)) and a refused list leaves the stored catalog alone. An answer the adapter cannot read is a rejected failure, not a server error. Two refreshes that add the same model both succeed: the save that loses the primary-key race reads the catalog again and updates the row.

Create, replace-key, and the two reads run under the tenant scope filter. Test and refresh wait on the provider, so they sit outside the filter and `ModelConnectionService` opens its own scopes: one to read the connection, reveal the key, and work out the space's rule; then the provider call with no scope open; then a new scope to write. If the key or address changed while the provider was answering, the answer is not written. Balances are stored on the connection as a list (Venice reports both `usd` and `diem`), each a snapshot from before the request; `RecordBalances` is the call a run uses after a reply.

The boundary preserves text, images, reasoning (including opaque protected reasoning), function declarations/calls/results, structured output, streaming, citations, and usage including cached/reasoning counts. Function results retain their JSON value shape; exception objects are replaced by a safe failure indication. Tool calls with an SDK argument-parse failure are rejected with a fixed message before the agent framework can execute them; a valid no-argument call remains valid. Unknown content/tool kinds, non-object tool arguments, background/continuation options and provider-specific option objects fail explicitly rather than disappearing. Agent history, approvals, tool invocation and orchestration continue to use Microsoft Agent Framework inside `Drover.Agents`.

| Kind | Auth | Adapter | Phase |
| --- | --- | --- | --- |
| `openai_compatible` | API key, or none for local models | `Drover.Adapters.OpenAICompatible` | 0 |
| `venice` | API key | `Drover.Adapters.Venice` | 0 |
| `anthropic` | API key | `Drover.Adapters.Anthropic` | 1 |
| `openai_signin` | The user's own ChatGPT plan, self-hosted only | `Drover.Adapters.OpenAISignIn` | 1 |
| `openrouter_signin`, `huggingface_signin`, `copilot_signin` | OAuth to the user's own account | One adapter each | 2 |

Every adapter must pass the model provider conformance suite: list models, plain reply, streaming, tool call round trip, structured output where claimed, reasoning text where claimed, a rate-limit response mapped to "retry later", an invalid key mapped to a clear setup message.

Each connection stores a **capability record per model**, in Drover's own shape: context size, maximum output, tool calling, vision, reasoning and its effort levels, structured output, built-in web search, price per million input and output tokens, privacy mode. The bot editor uses it to show only what a model can do, and cost tracking uses its prices. Providers that publish this are read automatically; for a generic compatible endpoint the operator fills in what matters, with sensible defaults.

### Images in requests

The one image a run sends today is a screenshot from the computer tools ([07-sandbox.md](07-sandbox.md#computer-use)). It crosses the boundary as `DataContent` in the agent framework, `ModelData` (bytes and media type) in Drover's transport, and, on the OpenAI-compatible wire that Venice shares, a user message whose content is a `text` part and an `image_url` part holding a `data:image/png;base64,...` URI, placed after the tool result, which stays text (`OpenAICompatibleTests.A_tools_picture_goes_out_as_an_image_url_data_uri_in_a_user_message_after_the_tool_result`). It is sent on one model call and never stored.

**The vision gate.** The run engine offers the computer tools only when the bot's model has `Vision` in its catalog entry. Venice fills it from its model list (`supportsVision`); a generic compatible endpoint claims no vision until the operator marks the model, so a model that cannot look is not handed a screen. Images a person uploads still do not reach the model: `MessageParts` leaves an image part out, and that waits for the file store.

## Venice, first-party

Venice is supported as a named provider with its own adapter, not as "a compatible endpoint that happens to work". Setup is: choose Venice, paste a key, done.

### What the adapter does

| Area | Design | Source fact |
| --- | --- | --- |
| Connection | Fixed base address. The setup screen asks only for the key. An inference key is all Drover needs; it never asks for an admin key. | Bearer key; two key types exist |
| Model discovery | Reads the public model list and traits. No key needed, so the model picker works before the key is saved. Refreshed daily and on demand. | The list endpoint is keyless and returns capabilities, context sizes, prices, privacy mode, and traits |
| Capability record | Filled from the list: tool calling, vision, reasoning with its effort options, structured output, web search, context and output limits, prices in dollars. | Fields exist per model |
| Defaults | The starter bot uses the provider's own "default" trait. Other traits (reasoning, code, vision, most capable) are offered as one-tap choices. | Traits endpoint |
| Chat | The OpenAI-compatible chat endpoint through the shared protocol transport in `Drover.Adapters.OpenAICompatible`. Streaming, tools, parallel tool calls, structured output, images. | "Any OpenAI SDK works" |
| Provider options | Sent with the documented model suffix form, `model:key=value&key=value`, so no custom request body is needed. | Documented suffix syntax |
| **System prompt** | The provider's own system prompt is switched **off** on every request. A Drover bot's instructions are fully the user's; nothing is prepended. | Default is on unless disabled |
| Reasoning | Reasoning text arrives in a non-standard field. The adapter maps it to Drover's `reasoning` message part. For models that return an encrypted placeholder, the part is marked "not available". Effort maps from the bot's provider-neutral `reasoning_effort`. | Field and placeholder documented |
| Built-in web search | A per-bot model setting: off, on, or automatic, with citations. Citations returned by the provider become source links on the message. This is separate from, and can be used alongside, Drover's own `IWebSearch` tool. | Three-state option; citations returned |
| Privacy mode | Each model's mode (private, anonymized, or attested) is shown as a badge in the model picker, in the provider's own terms, with a link to its explanation. A space setting can restrict bots to private or attested models only. | Published per model |
| Balance and limits | Response-specific balance headers are pre-request snapshots, not post-charge balances. Rate-limit headers drive backoff. A low-balance or limit error becomes a plain message on the run. | Headers documented |
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

### Verification and remaining live gate

- **Offline behavior is pinned.** Recorded fixtures and deterministic generated responses cover whole/streamed text, tool round trips, structured output, reasoning, fragmented encrypted placeholders, citations on empty-choices stream chunks, response-isolated balances, safe retries, cancellation and sanitized failures. The SDK still owns the wire protocol.
- **Documented options.** The [suffix guide](https://docs.venice.ai/api-reference/endpoint/chat/model_feature_suffix) documents `include_venice_system_prompt=false`, `enable_web_search=on|auto`, `include_search_results_in_stream=true`, and `disable_thinking=true`. Search requests enable streaming search results so citations are available on metadata chunks. `reasoning_effort` remains a body field. Provider values `max` and `minimal`, which the framework enum lacks, are preserved through the OpenAI SDK's extensible request option inside the protocol adapter. Some mandatory-reasoning models may still compute and bill reasoning tokens when thinking output is disabled.
- **Documented metadata.** The [rate-limit guide](https://docs.venice.ai/api-reference/rate-limiting) uses Unix milliseconds. The adapter validates timestamp ranges and waits for the latest valid request/token reset, including the failed-request reset when present. The [response-header reference](https://docs.venice.ai/api-reference/api-spec#response-headers-reference) documents USD/DIEM balance headers as pre-request snapshots. The [reasoning guide](https://docs.venice.ai/guides/features/reasoning-models) documents `[Some reasoning content is encrypted]`.
- **Connection testing.** The documented `GET /api_keys/rate_limits` accepts inference or admin keys and does not make a billed model call. Drover asks only for an inference key.
- **Timeout ownership.** SDK retries and its separate network timeout are disabled. The named HTTP client's resilience pipeline owns retry/timeout policy; POSTs are not replayed after an ambiguous server error or timeout. A pipeline test verifies the resolved SDK timeout without waiting for it.
- **Keyed verification remains a gate.** Hand-written fixtures say so and never claim to be captures. Explicit `Live` tests are excluded from normal and PR runs. The owner must run the keyed tests before production wiring/deployment; no keyed inference was performed to validate this change. Public model/trait discovery is a separate, keyless test.

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
