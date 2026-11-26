# 15. Model providers

Facts in this section were read from each provider's own documentation and live API on 2026-11-11.

## Shape

A **model connection** is data: a kind, an address, a way to authenticate, and what was learned about its models. `IModelClientProvider` turns a `ModelEndpoint` into a Drover-owned `IModelChatClient` and lists/tests the connection. `ModelConnection` remains the space-owned persistence entity, with no credentials in either shape. Embeddings remain future work. One adapter project per vendor package: a vendor package is referenced by exactly one `Drover.Adapters.*` project (AGENTS.md section 4), so every kind built on the OpenAI package (`openai_compatible`, `openai`, and the sign-in kinds that reuse its clients) lives in `Drover.Adapters.OpenAICompatible`, and a kind with its own package gets its own project.

The providers are wired into the server by `AddDroverModels` (`Drover.Server`), one `AddModelProvider<TProvider>` call each, the compatible transport before Venice, then OpenAI, and returned clients are bridged with `ModelChatClientBridge`. Every provider call uses a named HTTP client from `IHttpClientFactory`, and `AddModelProvider` puts `SpaceOutboundHandler` on each client name it is given, so the checked outbound handler of the deployment policy applies to model connections and an adapter cannot be registered without it. An adapter builds its own requests and knows nothing of policy, so the caller wraps the adapter call in `OutboundAttempt.Begin(effectivePolicy)` and `SpaceOutboundHandler` puts the space's `AllowPrivateAddresses` value on each request made inside it; a request made outside any attempt says nothing and is treated as forbidden. When the handler refuses an address, the adapter maps the refusal to a rejected failure that carries its own message, so the connection test says why instead of "could not be reached"; a refusal is one attempt, never retried and never counted toward the circuit breaker. No tenant transaction is held across these provider calls.

## Provider kinds

A provider is a `Drover.Adapters.*` project compiled into the server and added by one registration call ([ADR 0024](../adr/0024-providers-are-compiled-in-adapters.md)); Drover never loads provider assemblies at run time. `AddModelProvider<TProvider>(addAdapter, httpClients)` runs the adapter's own registration, checks that it registered `TProvider`, and adds `SpaceOutboundHandler` to every named HTTP client in `httpClients`. A new kind is one more call in `AddDroverModels` and a conformance test class.

**The descriptor.** `IModelClientProvider.Descriptor` is a `ModelProviderDescriptor` (`Drover.Domain.Models`): `Kind`, `DisplayName`, `KeyRule` (`Required`, `Optional`, `None`), `AddressRule` (`Fixed`, `Required`), `AddressHint`, `KeyHint`, and `ReportsCapabilities` (true when the provider's model list carries capabilities and prices, as Venice's does; a compatible endpoint's does not). It holds words and rules only: never a key, a header, an address of the provider, or a balance. `FixedAddress` stays a separate member of the provider and never leaves the server. The conformance suite checks that a provider's descriptor names its own kind and matches its behavior: the address rule is `Fixed` exactly when `FixedAddress` is set, and a provider whose key is `Required` refuses a missing key with the invalid-key setup message while one whose key is optional or none answers without one (`A_provider_describes_itself_with_a_kind_a_name_and_rules_that_match_its_behavior`).

| Kind | Name | Key | Address | Reports capabilities |
| --- | --- | --- | --- | --- |
| `openai_compatible` | Compatible endpoint | Optional | Required | No |
| `venice` | Venice | Required | Fixed | Yes |
| `openai` | OpenAI | Required | Fixed | No |

**The catalog.** `ModelProviderCatalog` (`Drover.Engine`, a singleton) holds the registered providers in registration order and answers which are offered. It is built when the server starts (`ModelProviderStartupCheck`): two providers with one kind, or a provider whose descriptor names another kind, stop the server with a sentence that names the kind. Every place that picks a provider for a connection asks it, so the deployment policy applies to a test, a refresh, a run, a screen reader, a summary, an image, and speech alike.

**The route.** `GET /api/model-provider-kinds` answers a signed-in person with the kinds this server offers, in registration order, as `ModelProviderKindResponse` (`Kind`, `DisplayName`, `KeyRule` and `AddressRule` as the words in `ModelKeyRules` and `ModelAddressRules`, the two hints, `ReportsCapabilities`). It is the same for everyone and is not in a space. A signed-out caller gets 401. `DroverApi.ListModelProviderKinds` is the client's call.

**Which kinds are offered.** The deployment policy rules `ModelProviders` and `AllowedModelProviderKinds` ([16-deployment-policies.md](16-deployment-policies.md)): under `All` every registered kind is offered; under `OperatorList` (the hosted preset, or an operator's choice) only the kinds in the list. A kind that is not registered or not offered cannot be given to a new connection (`POST /api/spaces/{spaceId}/model-connections` answers 400 with "The kind {kind} is not available on this server." or "The operator of this server switched {kind} off."). A connection that already exists keeps being listed, and its next call fails with "The operator of this server switched {kind} off." without reaching the provider. The rule only narrows: no space setting turns a kind back on.

## Connections in a space

A connection is created and managed under `/api/spaces/{spaceId}/model-connections`. Antiforgery applies to every state-changing request.

| Route | Who | What |
| --- | --- | --- |
| `GET /` | member | The space's connections. A connection says `hasKey`; the key is never returned. |
| `POST /` | admin | Create. `kind` is a registered provider kind that the deployment offers ([Provider kinds](#provider-kinds)); any other is refused with a sentence that names it. A fixed-address kind (Venice) takes no address; a compatible endpoint needs an `http`/`https` address with no credentials, query, or fragment. The key is optional and goes to `ISecretVault` as a `model_key`. |
| `PUT /{id}/key` | admin | Store a new key as a new secret and delete the old one, then return the connection to `pending`. |
| `POST /{id}/test` | admin | Ask the provider whether the connection works. Records `ready` or `failed`, the time, and any balances. A rate-limit answer leaves the status alone. A passing test makes a second provider call to list models and refreshes the stored catalog as `POST /{id}/models/refresh` does. A failed listing leaves both the catalog and the test result alone. |
| `GET /{id}/models` | member | The stored catalog (`model_catalog`), in Drover's own shape. |
| `POST /{id}/models/refresh` | admin | Discover the provider's models and upsert the catalog. A model that is no longer listed keeps its row, marked `missing`. An empty list marks nothing missing. If the provider cannot answer, the stored catalog is returned with the reason. |

Creating a connection to a literal private address is refused when the space or the deployment forbids private addresses; a host name is checked each time a connection is made, because what it resolves to can change.

Model discovery is bounded. Adapters read a model list with headers first and stop at `MaxModelListBytes`; `ModelConnectionService` refuses a list with more than `MaxModelsPerConnection` models. Both are deployment policy rules ([16-deployment-policies.md](16-deployment-policies.md)) and a refused list leaves the stored catalog alone. An answer the adapter cannot read is a rejected failure, not a server error. Two refreshes that add the same model both succeed: the save that loses the primary-key race reads the catalog again and updates the row.

Create, replace-key, and the two reads run under the tenant scope filter. Test and refresh wait on the provider, so they sit outside the filter and `ModelConnectionService` opens its own scopes: one to read the connection, reveal the key, and work out the space's rule; then the provider call with no scope open; then a new scope to write. A passing test records its result before making a second provider call to list models, also with no scope open, then writes the catalog in another scope using the same upsert as refresh. A failed listing leaves the stored catalog and the recorded test result alone. If the key or address changed while either provider call was answering, that call's answer is not written. Balances are stored on the connection as a list (Venice reports both `usd` and `diem`), each a snapshot from before the request; `RecordBalances` is the call a run uses after a reply.

The boundary preserves text, images, reasoning (including opaque protected reasoning), function declarations/calls/results, structured output, streaming, citations, and usage including cached/reasoning counts. Function results retain their JSON value shape; exception objects are replaced by a safe failure indication. Tool calls with an SDK argument-parse failure are rejected with a fixed message before the agent framework can execute them; a valid no-argument call remains valid. Unknown content/tool kinds, non-object tool arguments, background/continuation options and provider-specific option objects fail explicitly rather than disappearing. The Venice adapter applies a bot’s JSON-only reply setting only when the request cannot call tools. An explicit response format still takes precedence. Agent history, approvals, tool invocation and orchestration continue to use Microsoft Agent Framework inside `Drover.Agents`.

| Kind | Auth | Adapter | Phase |
| --- | --- | --- | --- |
| `openai_compatible` | API key, or none for local models | `Drover.Adapters.OpenAICompatible` | 0 |
| `venice` | API key | `Drover.Adapters.Venice` | 0 |
| `openai` | API key, on the Responses API ([OpenAI](#openai-on-the-responses-api)) | `Drover.Adapters.OpenAICompatible` | 1 |
| `anthropic` | API key | `Drover.Adapters.Anthropic` | 1 |
| `openai_signin` | The user's own ChatGPT plan, self-hosted only | `Drover.Adapters.OpenAICompatible` | 1 |
| `openrouter_signin`, `huggingface_signin`, `copilot_signin` | OAuth to the user's own account | One adapter each | 2 |

Every adapter must pass the model provider conformance suite: list models, plain reply, streaming, tool call round trip, structured output where claimed, reasoning text where claimed, protected reasoning sent back unchanged in a tool loop where claimed, cached input tokens reported where claimed, a rate-limit response mapped to "retry later", an invalid key mapped to a clear setup message. The suite reads the wire through `IChatWire` (`tests/Drover.Adapters.Conformance/ChatWire.cs`), which each adapter's test class gives on its `ProviderUnderTest`: which requests are model calls, the tool names offered, the text sent back for a tool call, the structured output asked for, and the protected reasoning sent back. `ChatCompletionsWire` is the default and serves the compatible and Venice adapters; `ResponsesWire` serves OpenAI. `ConformanceClaims` says which optional features an adapter's test model has: `StructuredOutput`, `ReasoningText`, `ProtectedReasoning` and `CachedUsage`.

Each connection stores a **capability record per model**, in Drover's own shape: context size, maximum output, tool calling, vision, reasoning and its effort levels, structured output, built-in web search, price per million input and output tokens, privacy mode. The bot editor uses it to show only what a model can do, and cost tracking uses its prices. Providers that publish this are read automatically; for a generic compatible endpoint the operator fills in what matters, with sensible defaults.

### Images in requests

The one image a run sends today is a screenshot from the computer tools ([07-sandbox.md](07-sandbox.md#computer-use)). It crosses the boundary as `DataContent` in the agent framework, `ModelData` (bytes and media type) in Drover's transport, and, on the OpenAI-compatible wire that Venice shares, a user message whose content is a `text` part and an `image_url` part holding a `data:image/png;base64,...` URI, placed after the tool result, which stays text (`OpenAICompatibleTests.A_tools_picture_goes_out_as_an_image_url_data_uri_in_a_user_message_after_the_tool_result`). It is sent on one model call and never stored.

### Image generation

`image.create` is a built-in write tool. A bot sees it only after a person grants it and only where `AllowImageGeneration` permits it. The tool uses the bot's model connection, requires an image model name, and asks the connection's image adapter for one PNG. The compatible wire sends one bounded request to `images/generations` and accepts one bounded base64 PNG.

The generated bytes use the normal tenant-scoped file store. The file row carries the conversation's audience and the bot as creator, counts against the existing file limits, and becomes an image part of the reply that requested it. The tool call id is the file id, so a replay finds the existing file without generating or charging for another image. Provider-reported tokens and price become a normal usage record. A catalog's flat per-image USD price is recorded as a catalog estimate. Tiered image prices stay unpriced because this tool does not yet represent the provider's resolution and quality tiers. Any other provider that gives no reliable price also produces an unpriced usage record rather than a made-up zero.

**The vision gate.** The run engine offers the computer tools only when the bot's model has `Vision` in its catalog entry, or the bot has a screen reader set. Venice fills it from its model list (`supportsVision`); a generic compatible endpoint claims no vision until the operator marks the model, so a model that cannot look, with nothing to look for it, is not handed a screen.

**The screen reader's call.** A screen reader is another model of the space, chosen in the bot editor's Model section, whose catalog entry has `Vision`; it describes each screenshot so the bot's model gets words and never the picture ([07-sandbox.md](07-sandbox.md#computer-use)). `ScreenDescriber` (`Drover.Engine`) makes the call the way a run makes its own: it reads the connection, the catalog entry, the key, and the space's effective policy in a short tenant transaction, closes it, picks the `IModelClientProvider` for the connection's kind (so `ModelProviders` and every other provider the server does not register stay out), and calls `IModelChatClient.GetResponse` once inside `OutboundAttempt.Begin`, so `AllowPrivateAddresses` of the deployment and the space applies to it as to the bot's own calls. The request is `ModelChatOptions.Instructions` (Drover's fixed text, which the compatible transport sends as the system message: `OpenAICompatibleTests.A_screen_readers_request_goes_out_as_the_instruction_then_one_user_message_with_the_picture_and_no_tools`), one user message with text and a `ModelData` PNG, `MaxOutputTokens` 1000, no tools. It counts against the run's model calls (`MaxStepsPerRun` and `MaxTokensPerRun` as the run has them, through `BuiltinToolCall.BeginModelCall`; the count is held by the execution, so after a restart only the bot's own steps are counted again) and is bounded by `RunOptions.ScreenReadTimeout`. A provider failure is the tool's error in words; nothing falls back. No deployment rule was added: the reader is a model connection of the same space, called under every rule that binds the bot's own model, paid by the same space's key, and a screen exists only where `AllowDesktopSandboxes` allows one.

**Its usage.** The usage the reader reports is recorded against the run exactly as the run's own calls are (`RunGovernor`, through `BuiltinToolCall.RecordModelUsage`): a `usage_records` row with the run, the root run, and the bot, priced from the reader's catalog entry, a `usage.recorded` event at the current step, and the run's totals. The row's `model_connection_id` and `model` are the reader's, not the bot's, which is how a person tells the two apart; the event carries no model and was not changed. A picture a person attaches reaches the bot's own model, when its catalog entry says it reads images; the screen reader is not used for it ([02-data-model.md](02-data-model.md#sandboxes-files-secrets-audit)).

### Stored fallback chains

A bot keeps up to five ordered fallback models in `model_settings.fallbacks`. Each entry names a model connection and
model. `use_space_fallbacks` defaults to true, so a bot with no list of its own uses the space's
`settings.model_fallbacks` list unless it opts out. Saving a fallback requires a connection from the same space and a
catalog model whose state is not `missing`. The primary model and fallback entries cannot repeat.

`GET /api/spaces/{spaceId}/bots/{botId}/model-chain` describes the effective chain without running it. It returns the
primary model followed by the bot list or space list, with connection state, capabilities, token prices, privacy mode,
and plain sentences for capability mismatches. A stored entry whose connection no longer exists is skipped when the
chain is read. Runtime switching between entries belongs to the run engine, not the stored settings API.

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

## OpenAI, on the Responses API

The `openai` kind connects OpenAI with an API key and nothing else. `OpenAIProvider` and `ResponsesChatClient` live in `Drover.Adapters.OpenAICompatible`, the one project that references the vendor package. Its address is fixed at `https://api.openai.com/v1/`: a connection carries none, and the connection's own address is never used. It has its own named client, `drover-openai`, with the same resilience rules as the compatible client and `SpaceOutboundHandler` added by `AddModelProvider`. A connection made with the compatible kind and OpenAI's address keeps chat completions; this kind is separate.

| What | How |
| --- | --- |
| Calls | `GET /v1/models` to list and test; `POST /v1/responses` for every model call, whole or streamed. The key goes as `Authorization: Bearer` |
| Nothing stored at the provider | Every request sends `store: false` and never `previous_response_id`: Drover owns the conversation and sends it whole in `input` each time. This is not a setting |
| Reasoning carried by Drover | With `store: false` each reasoning item comes back with `encrypted_content`, which arrives as `ModelReasoning.ProtectedData` and goes back unchanged as a reasoning item in the next call of the same tool loop. The item's id is not sent, because the provider keeps no item to look it up in. Reasoning held only as words (a stream's summary text, or reasoning text kept in a thread's history) is not sent back. `include` is not sent: a stateless request gets the encrypted content by default, and a model that does not reason refuses the include. Keeping protected reasoning in a thread's history between turns is #536 |
| Reasoning words | `reasoning.summary: "auto"` on every request, so `ModelReasoning.Text` has words. A model that does not reason accepts it and ignores it. When the provider answers 400 naming the summary (an organization not yet verified for summaries, or a model that refuses the setting), the same call is made once more without it, before anything is shown; any other 400, or a second refusal, is the call's failure. `reasoning.effort` is sent only when the bot or the call sets one, as given: the values each model takes differ, and the provider's own refusal says which |
| Other settings | `MaxOutputTokens` to `max_output_tokens`, `AllowMultipleToolCalls` to `parallel_tool_calls`, instructions to `instructions`, a schema to `text.format` (`json_schema`), the bot's JSON switch to `text.format` `json_object`. Function tools are flat (`type`, `name`, `description`, `parameters`); a call comes back as `function_call` and its result goes back as `function_call_output`, matched by `call_id` |
| Files | Pictures go as `input_image` parts. Any other file is refused in words before anything is sent; the engine gives a model other files as text. Native PDF input is a later change |
| Usage | `InputTokens`, `OutputTokens`, `CachedInputTokens` from `input_tokens_details.cached_tokens`, `ReasoningTokens` from `output_tokens_details.reasoning_tokens`; a count the provider does not send stays null |
| Endings | `status: "incomplete"` (or a `response.incomplete` event) ends as `length`, or `content_filter` when the provider says the filter cut it. A refusal part is the reply's text and ends as `content_filter`. A whole answer with `status: "failed"`, and a `response.failed` or `error` event in a stream, is a failure of the call, mapped by its code (`rate_limit_exceeded`, `slow_down` and `server_is_overloaded` to retry later, `insufficient_quota` to out of credit, `context_length_exceeded` to too long, `server_error` to unreachable, anything else rejected); the provider's message is not repeated, since it can quote the input |
| Failures over HTTP | As for the compatible client: 401 and 403 are an invalid key, 429 is retry later with the provider's `Retry-After`, `context_length_exceeded` is too long |

**In the connect form.** The client offers this kind from the server's list of kinds, after Venice, with the kind's name and key hint. The `openai` named provider of the compatible kind (chat completions at the same address) has the same name, so the form leaves it out and offers OpenAI once; it stays on the server's list.

**Every model starts permissive.** The model list returns `id`, `object`, `created` and `owned_by` and nothing about what a model can do, and it has no type field, so embedding, speech and image models are listed too and are not filtered by name. Every listed model starts with tool calling, streaming, vision, structured output and reasoning (efforts `low`, `medium`, `high`), so a bot can be given a picture and an effort with no admin step. A setting a model does not take comes back as the provider's own sentence on the run. Context size and prices stay unknown: a bot works without them, and an admin can fill them in (#531).

**Test.** Without a key: "Paste an OpenAI API key to finish setup." With one, the model list is read with it, and success is "Connected to OpenAI."

**Per call address and headers.** `ResponsesChatClient` takes a `ResponsesDestination` for each client it builds: the base address, the key or token sent as the bearer credential, and any extra headers. `OpenAIProvider` gives the fixed address, the key and no headers. A sign-in kind on this wire ([Signing in with a subscription](#signing-in-with-a-subscription)) gives its own address, the person's token and the headers its provider asks for, with no other change to the client. The destination is fixed for each client the provider builds, so a header that changes from request to request, or a token refreshed in the middle of a run, needs a per-request step added when such a kind is built. The vendor stack adds its own words to a `User-Agent` header given here.

Sources, read 2026-11-26: the create reference (https://developers.openai.com/api/reference/resources/responses/methods/create), the guides on reasoning, function calling, structured outputs and streaming under https://developers.openai.com/api/docs/guides/, and the model list reference (https://developers.openai.com/api/reference/resources/models/methods/list). The wire tests (`OpenAIResponsesWireTests`) and the conformance fixtures under `tests/Drover.Adapters.Tests/Fixtures/openai-responses/` are hand-written from those pages; `OpenAIResponsesLiveTests` is the live check the owner runs with a key.

## Generic compatible endpoints

One base address and an optional key covers most hosted providers and local model servers. The adapter lists models from the endpoint when it can and otherwise lets the operator type a model name. Capabilities default to "tools and streaming, nothing else claimed" until the operator says more, so a bot is never offered a feature the model lacks.

The operator says more one model at a time, and today the one thing to say is that a model reads pictures. On the Models page a space admin turns on "<model> reads pictures" for a model of a compatible connection (`PUT /api/spaces/{space}/model-connections/{connection}/models/vision`, admin only, in the space's tenant scope; the model is named in the body because a model ID can hold slashes, and turning the switch off clears the mark). The mark is stored in the catalog row's capabilities as `VisionSetByOperator`, beside `Vision`, so it needs no migration. The catalog's `Vision` is the value every other part reads: the attachment hint, the upload check that decides whether a picture is taken as a picture, and the run that hands the model an image part. A catalog refresh replaces the capabilities with what the provider reports, which for a compatible endpoint is nothing, but keeps the mark (`CatalogUpsert`). A provider that reports its own capabilities, such as Venice, keeps its own value, offers no switch, and refuses the request. A person who is not an admin sees which models read pictures but cannot change it. A refresh and a mark each write the whole capabilities value of a catalog row, so each reads the rows it writes with a row lock (`SELECT ... FOR UPDATE`) and the second to arrive waits for the first and reads what it left. A refresh that overlaps a mark keeps the mark, and a mark that overlaps a refresh keeps what the refresh reported (`ModelVisionTests`). A model that is marked reads pictures only as far as the endpoint really does: a mark on a model that cannot read them fails at the provider, not in Drover.

### Named providers

A person who does not want to type an address picks a provider by name. A named provider is a starting point, not a kind: the stored kind stays `openai_compatible` and the connection keeps its `BaseUrl`, so every adapter lookup, the vision and voice marks and the speech and image paths work as they do for a hand-typed address. A provider whose address changes is fixed by editing one row of the connection, and a provider that needs its own wire gets its own kind.

The table is `KnownProviders` in `Drover.Adapters.OpenAICompatible`, one compiled-in list of `ModelProviderPreset` records (`Drover.Domain`): `Id`, `DisplayName`, `Address`, `KeyRule` (`Required`, `Optional`, `None`), `KeyPage` (https only, or null) and `Local` (the address is on the person's own machine or network). Nothing else in code repeats an address, and nothing at run time adds or changes a row: an address a server calls with a person's key is not taken from a file fetched over the network. The compatible kind's `ModelProviderDescriptor` carries the table as `NamedProviders`, and `GET /api/model-provider-kinds` (signed in) returns it with that kind, in table order, as `ModelProviderPresetResponse` rows. `ModelProviderCatalog.Offered` leaves out the `Local` rows where the deployment's `AllowPrivateAddresses` is false, and the whole kind (so its named providers) where `AllowedModelProviderKinds` does not offer it. Create is unchanged: the client sends `kind: openai_compatible`, the row's address and the key, and `ModelConnectionService` applies every check it applies to any address.

The order is OpenAI, OpenRouter, Google Gemini, xAI, Groq, DeepSeek, Mistral, the other hosted providers by name, then the local ones. Each row's comment in `KnownProviders.cs` names the documentation page that confirmed its address on 2026-11-26. Key pages are confirmed for Groq, DeepSeek, Cerebras, Nebius and Together only. `KnownProvidersLiveTests` lists models on each row whose key is in the environment (`DROVER_TEST_KEY_{ID}`); it settles what a page cannot.

| Id | Name | Address | Key | Where | Address on 2026-11-26 |
| --- | --- | --- | --- | --- | --- |
| `openai` | OpenAI | `https://api.openai.com/v1` | required | hosted | confirmed |
| `openrouter` | OpenRouter | `https://openrouter.ai/api/v1` | required | hosted | confirmed |
| `gemini` | Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai/` | required | hosted | confirmed |
| `xai` | xAI | `https://api.x.ai/v1` | required | hosted | confirmed |
| `groq` | Groq | `https://api.groq.com/openai/v1` | required | hosted | confirmed |
| `deepseek` | DeepSeek | `https://api.deepseek.com` | required | hosted | confirmed |
| `mistral` | Mistral | `https://api.mistral.ai/v1` | required | hosted | path confirmed; the page also names a preview address (`/openai/v1`), for the Live run to settle |
| `alibaba` | Alibaba | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1` | required | hosted | confirmed, but the provider is moving to a workspace-specific address (slice 2 candidate) |
| `baseten` | Baseten | `https://inference.baseten.co/v1` | required | hosted | confirmed |
| `cerebras` | Cerebras | `https://api.cerebras.ai/v1` | required | hosted | confirmed |
| `chutes` | Chutes | `https://llm.chutes.ai/v1` | required | hosted | confirmed |
| `cohere` | Cohere | `https://api.cohere.ai/compatibility/v1` | required | hosted | confirmed |
| `deepinfra` | Deep Infra | `https://api.deepinfra.com/v1/openai` | required | hosted | confirmed |
| `digitalocean` | DigitalOcean | `https://inference.do-ai.run/v1` | required | hosted | confirmed |
| `fireworks` | Fireworks AI | `https://api.fireworks.ai/inference/v1` | required | hosted | confirmed |
| `friendli` | Friendli | `https://api.friendli.ai/serverless/v1` | required | hosted | confirmed |
| `helicone` | Helicone | `https://ai-gateway.helicone.ai/v1` | required | hosted | confirmed |
| `huggingface` | Hugging Face | `https://router.huggingface.co/v1` | required | hosted | confirmed |
| `inception` | Inception | `https://api.inceptionlabs.ai/v1/` | required | hosted | confirmed; the provider winds this API down through 2026-12-28, remove the row after |
| `moonshot` | Moonshot AI | `https://api.moonshot.ai/v1` | required | hosted | confirmed |
| `nebius` | Nebius Token Factory | `https://api.tokenfactory.nebius.com/v1` | required | hosted | confirmed |
| `novita` | Novita AI | `https://api.novita.ai/openai` | required | hosted | confirmed |
| `nvidia` | Nvidia | `https://integrate.api.nvidia.com/v1` | required | hosted | confirmed |
| `ollama-cloud` | Ollama Cloud | `https://ollama.com/v1` | required | hosted | confirmed |
| `ovhcloud` | OVHcloud AI Endpoints | `https://oai.endpoints.kepler.ai.cloud.ovh.net/v1` | required | hosted | confirmed |
| `perplexity` | Perplexity | `https://api.perplexity.ai/orchestrator/v1` | required | hosted | confirmed |
| `poe` | Poe | `https://api.poe.com/v1` | required | hosted | confirmed |
| `requesty` | Requesty | `https://router.requesty.ai/v1` | required | hosted | confirmed |
| `sambanova` | SambaNova | `https://api.sambanova.ai/v1` | required | hosted | confirmed |
| `scaleway` | Scaleway | `https://api.scaleway.ai/v1` | required | hosted | confirmed |
| `siliconflow` | SiliconFlow | `https://api.siliconflow.com/v1` | required | hosted | confirmed |
| `synthetic` | Synthetic | `https://api.synthetic.new/openai/v1` | required | hosted | confirmed |
| `together` | Together AI | `https://api.together.ai/v1` | required | hosted | confirmed |
| `upstage` | Upstage | `https://api.upstage.ai/v1` | required | hosted | confirmed |
| `vercel` | Vercel AI Gateway | `https://ai-gateway.vercel.sh/v1` | required | hosted | confirmed |
| `vultr` | Vultr | `https://api.vultrinference.com/v1` | required | hosted | confirmed |
| `zai` | Z.AI | `https://api.z.ai/api/paas/v4` | required | hosted | confirmed |
| `zai-coding` | Z.AI Coding Plan | `https://api.z.ai/api/coding/paas/v4` | required | hosted | confirmed |
| `lmstudio` | LM Studio | `http://127.0.0.1:1234/v1` | none | local | confirmed |
| `ollama` | Ollama | `http://localhost:11434/v1` | none | local | confirmed |
| `vllm` | vLLM | `http://localhost:8000/v1` | optional | local | confirmed |
| `llamacpp` | llama.cpp | `http://localhost:9931/v1` | optional | local | confirmed |

Left out: GitHub Models (retired on 2026-07-30) and Anthropic (waits for its own kind, #527). The Google Gemini row goes when #529 lands its own kind.

On the hosted service the base address is checked against private address ranges ([08-auth-and-secrets.md](08-auth-and-secrets.md)). Self-hosters can point at a machine on their own network.

## Voice

Speech has three jobs, each behind an interface Drover owns in `Drover.Domain/Voice`, and each chosen on its own:

| Interface | Job | Over the wire |
| --- | --- | --- |
| `ISpeechToText` | Write down a recording | `POST {base}/audio/transcriptions`, multipart, bounded by `MaxVoiceRecordingBytes` |
| `ITextToSpeech` | Read text aloud, streamed as it is made, and list a model's voices | `POST {base}/audio/speech`; `GET {base}/audio/voices` where the server has it |
| `IRealtimeVoice` | Hold a live call with a speech-to-speech model | One WebSocket at `{base}/realtime?model=` |

One adapter serves all three for a compatible connection (`OpenAICompatibleProvider`), so a provider works when its address does. No package was added: the adapter speaks the protocol with `HttpClient` and `ClientWebSocket`, through the same checked outbound handler as every other provider call, with the space's private-address rule on each request.

| Provider | Base address | Listens | Speaks | Live calls |
| --- | --- | --- | --- | --- |
| OpenAI | `https://api.openai.com/v1` | yes | yes | yes |
| Groq | `https://api.groq.com/openai/v1` | yes | yes: 200 characters a request and WAV only, which Drover handles (below) | no |
| xAI | `https://api.x.ai/v1` | no | no | yes. Its own speech and transcription endpoints are not this shape |
| Ollama | `http://host:11434/v1` | where the model does | where the model does | no |
| Kokoro-FastAPI | `http://host:8880/v1` | no | yes | no |
| Speaches | `http://host:8000/v1` | yes | yes | no |

Anthropic has no speech API, so a space whose bots answer with its models still names another connection, or the server's own speech service, for voice.

**Which models do what.** A compatible endpoint lists model names and nothing about them, so the adapter marks a model from its name when the catalog is refreshed (`KnownVoices.Mark`): names with `whisper`, `transcribe`, `stt` or `parakeet` listen; names with `tts`, `kokoro` or `orpheus` speak; names with `realtime`, or that begin `grok-voice`, hold live calls. An unknown name gets no mark. A space admin sets or corrects the three marks for one model with `PUT /api/spaces/{space}/model-connections/{connection}/models/voice` (body `{ model, speechToText, textToSpeech, realtimeVoice }`, compatible connections only, admin only). A mark set by hand is kept across refreshes (`VoiceSetByOperator`); all three false clears it.

**Which model a space uses.** `spaces.settings.voice` holds up to three choices, `listen`, `speak` and `live`, each a connection, a model and optionally a voice (`PUT .../voice/settings`, admin only; a connection of another space or a model not marked for the slot is refused with a sentence). A slot left empty is chosen by the server (`VoiceChoices.Resolve`): the first connection by name that has not failed and has an available model with the mark, the first such model by name, and the first voice the adapter lists. If the space has none, the server's own speech service is used. `GET .../voice/options` returns what each slot can use, what it uses now, whether that was chosen automatically, whether its address reads as one on a private network, and the three limits.

**The server's own speech service.** An operator may name a compatible speech endpoint for the whole server: `Drover:Voice:Speech:BaseUrl` (with `:Model`, default `kokoro`, and `:Voice`, default `af_heart`) reads aloud, and `Drover:Voice:Transcription:BaseUrl` (with `:Model`, required) writes down recordings. `compose.voice.yaml` runs a speech container beside Drover and sets the first, so read aloud works with nothing to set up. It appears in every space's options as "This server" with no connection id, is used only when the space has no model of its own for the slot, and holds no live calls. Its adapter (`OperatorSpeech`, kind `server_speech`) is the same protocol on a client of its own that follows no redirect and marks each request as going to an operator-configured address (`OperatorAddressHandler`), exactly as the search service does ([16-deployment-policies.md](16-deployment-policies.md)). Because the address is the operator's own, it is reached on a private address even where `AllowPrivateAddresses` is off, so the speech container beside the server works on a hosted server and on one with open registration. No space, person or bot can create a connection of that kind or change where it points. A value that is not an http or https address, or a transcription address with no model, stops the server and names the setting.

**The endpoints** are under `/api/spaces/{space}/voice`, open to members, and sit outside the tenant scope filter because each waits on a provider. `VoiceService` opens short transactions of its own as the person who asked (read the choice, reveal the key, work out the space's outbound rule; close), calls the adapter under `OutboundAttempt.Begin`, and never holds a transaction across the wait.

- `POST /transcribe` takes the recording as the raw body with its `Content-Type` (`audio/webm`, `audio/wav`, `audio/mp4`, `audio/mpeg`, `audio/ogg`) and answers `{ text }`. A body declared larger than `MaxVoiceRecordingBytes` is refused before any of it is read, and one sent without a length is cut off at the same limit while it is read; both answer 413 with a sentence. The audio goes from the request to the provider through memory. It is never written to disk, to the database or to a log, and neither are its words.
- `POST /speak` takes `{ messageId }` or `{ text }` (a sample of a voice), never both, with an optional `voice` and `format` (`mp3` by default, `wav`, `opus`, `pcm`). A message is read inside a transaction as the caller, so row-level security decides whether they may: a message of another space or of another member's private conversation is 404. Only its text parts are spoken, never reasoning, tool calls or tool results. Text past `MaxReadAloudCharacters` is refused with a sentence. The audio is streamed as the provider makes it, with the format's media type; nothing is sent until the first of it arrives, so a provider that refuses is still answered with a problem.
- Long text is cut into pieces a provider takes in one request (`SpeechText.Split`): at the end of a sentence where it can, at a space where it cannot. A piece is 4,000 characters, or 200 for `api.groq.com`, and the pieces are asked for in order. Several pieces are asked for as MP3 whatever was requested, because MP3 answers play when put end to end and other containers do not. `api.groq.com` answers only in WAV, so it is always asked for WAV, and its answers are joined (`WavJoin`): the first 44-byte header is kept with both lengths set to `0xFFFFFFFF` (unknown, as a stream's are) and the header of each later answer is dropped.
- A provider failure (`ModelProviderException`) answers 502 with the adapter's own sentence, never the provider's text. Nothing set up for the slot answers 409 with what to do: "Reading aloud needs a speech model. Connect one under Models."

**Usage is not recorded yet.** Usage rows belong to runs (`runs.usage`), and transcription, read aloud and calls happen outside any run, so today the server logs ids, byte and character counts, durations and a call's token counts, and stores no cost. Recording speech usage needs a place for usage that has no run, which is its own change.

Live calls are described in [03-run-lifecycle.md](03-run-lifecycle.md#live-calls).

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
