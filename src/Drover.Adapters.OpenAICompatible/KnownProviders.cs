namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The named providers a person can pick instead of typing an address. This is the one place an address is written
/// down: the table is compiled in and reviewed, and nothing at run time adds or changes a row. A connection made from
/// a row is stored as an ordinary compatible connection, so every check on a hand-typed address applies to it.
///
/// The order is the order a person sees: a short lead of the best-known providers, then the other hosted providers
/// by name, then the ones on the person's own machine or network.
///
/// Each row's comment names the documentation page that confirmed its address on 2026-11-26 (the pages for the rows
/// confirmed in review are the ones that review named). Key pages are confirmed for Groq, DeepSeek, Cerebras, Nebius and
/// Together; the others are the providers' well-known console addresses and are unconfirmed, the xAI and Mistral ones
/// especially. KnownProvidersLiveTests is the owner's run that settles what a page cannot. Left out because their
/// documentation says so: GitHub Models (retired on 2026-07-30, https://docs.github.com/en/rest/models/inference).
/// </summary>
public static class KnownProviders
{
    #region Patterns

    // None of these allows / @ : ? # or white space, so a value cannot end the host or start a path.

    private const string Word = "^[A-Za-z0-9-]{1,63}\\z";

    private const string Host = "^[A-Za-z0-9-]+";

    private const string Region = "^[a-z]{2}(?:-[a-z]+)+-[0-9]{1,2}\\z";

    #endregion

    #region Rows

    /// <summary>Every row, in the order offered.</summary>
    public static IReadOnlyList<ModelProviderPreset> All { get; } =
    [
        // https://platform.openai.com/docs/api-reference/introduction. Not listed while the server offers the openai
        // kind (the provider's Responses API, OpenAIProvider): a named provider whose own kind is offered is left out.
        Hosted("openai", "OpenAI", "https://api.openai.com/v1", "https://platform.openai.com/api-keys"),

        // https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion
        Hosted("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "https://openrouter.ai/settings/keys"),

        // https://ai.google.dev/gemini-api/docs/openai. When #529 offers its own kind with this id, the server stops
        // listing this row, as it does for openai.
        Hosted("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", "https://aistudio.google.com/apikey"),

        // https://docs.x.ai/developers/model-capabilities/legacy/chat-completions
        Hosted("xai", "xAI", "https://api.x.ai/v1", "https://console.x.ai"),

        // https://console.groq.com/docs/openai
        Hosted("groq", "Groq", "https://api.groq.com/openai/v1", "https://console.groq.com/keys"),

        // https://api-docs.deepseek.com/ (base_url for the OpenAI format).
        Hosted("deepseek", "DeepSeek", "https://api.deepseek.com", "https://platform.deepseek.com/api_keys"),

        // https://docs.mistral.ai/inference/conversations/chat-completion shows POST /v1/chat/completions with a Bearer key, and
        // the API reference lists /v1/models. The page also names https://api.mistral.ai/openai/v1 as a preview endpoint for OpenAI
        // clients; the GA address is kept. Which one answers the compatible protocol is settled by KnownProvidersLiveTests.
        Hosted("mistral", "Mistral", "https://api.mistral.ai/v1", "https://console.mistral.ai/api-keys"),

        // https://www.alibabacloud.com/help/en/model-studio/compatibility-of-openai-with-dashscope and
        // https://www.alibabacloud.com/help/en/model-studio/base-url (Singapore, workspace-dedicated domain). The page asks to migrate
        // from dashscope-intl.aliyuncs.com, which stays available, to this address; the Workspace ID is on the workspace details page of
        // the console. A key belongs to one region, so this row is the Singapore one.
        Hosted("alibaba", "Alibaba", "https://{blank}.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1", blank: new("Workspace ID", Word)),

        // https://docs.aws.amazon.com/bedrock/latest/userguide/inference-chat-completions.html (bedrock-runtime, recommended; API key as a
        // Bearer token). That endpoint has no GET /models, so a test reports that the endpoint lists no models and the model name is typed.
        Hosted("bedrock", "Amazon Bedrock (by key)", "https://bedrock-runtime.{blank}.amazonaws.com/openai/v1", blank: new("Region", Region)),

        // https://docs.baseten.co/reference/inference-api/chat-completions
        Hosted("baseten", "Baseten", "https://inference.baseten.co/v1"),

        // https://inference-docs.cerebras.ai/resources/openai (key page: https://inference-docs.cerebras.ai/console/api-keys)
        Hosted("cerebras", "Cerebras", "https://api.cerebras.ai/v1", "https://cloud.cerebras.ai"),

        // https://chutes.ai/llms.txt
        Hosted("chutes", "Chutes", "https://llm.chutes.ai/v1"),

        // https://developers.cloudflare.com/workers-ai/configuration/open-ai-compatibility/ (POST .../ai/v1/chat/completions, Bearer API token).
        // The account ID is 32 hexadecimal characters. No GET /models is documented, so the model name is typed.
        Hosted("cloudflare-workers-ai", "Cloudflare Workers AI", "https://api.cloudflare.com/client/v4/accounts/{blank}/ai/v1", blank: new("Account ID", "^[0-9a-fA-F]{32}\\z")),

        // https://docs.cohere.com/docs/compatibility-api
        Hosted("cohere", "Cohere", "https://api.cohere.ai/compatibility/v1", "https://dashboard.cohere.com/api-keys"),

        // https://docs.databricks.com/aws/en/ai-gateway/query-model-services (base_url https://<workspace-url>/ai-gateway/mlflow/v1, Bearer
        // personal access token). The workspace host has dots, so its pattern needs one. GET /models is not documented there: unconfirmed.
        Hosted("databricks", "Databricks", "https://{blank}/ai-gateway/mlflow/v1", blank: new("Workspace host", Host + "(?:\\.[A-Za-z0-9-]+)+\\z")),

        // https://deepinfra.com/docs/openai_api
        Hosted("deepinfra", "Deep Infra", "https://api.deepinfra.com/v1/openai"),

        // https://docs.digitalocean.com/products/inference/how-to/si-endpoints/
        Hosted("digitalocean", "DigitalOcean", "https://inference.do-ai.run/v1"),

        // https://docs.fireworks.ai/tools-sdks/openai-compatibility (written without the trailing slash there).
        Hosted("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1"),

        // https://friendli.ai/docs/guides/serverless_endpoints/openai-compatibility
        Hosted("friendli", "Friendli", "https://api.friendli.ai/serverless/v1"),

        // https://docs.helicone.ai/rest/ai-gateway/post-v1-chat-completions
        Hosted("helicone", "Helicone", "https://ai-gateway.helicone.ai/v1"),

        // https://huggingface.co/docs/inference-providers/tasks/chat-completion
        Hosted("huggingface", "Hugging Face", "https://router.huggingface.co/v1", "https://huggingface.co/settings/tokens"),

        // https://docs.inceptionlabs.ai/get-started/get-started. The provider's Mercury API is winding down through
        // 2026-12-28: remove this row after that date.
        Hosted("inception", "Inception", "https://api.inceptionlabs.ai/v1/"),

        // https://platform.moonshot.ai/docs/guide/start-using-kimi-api (base_url in the OpenAI example).
        Hosted("moonshot", "Moonshot AI", "https://api.moonshot.ai/v1"),

        // https://docs.tokenfactory.nebius.com/quickstart
        Hosted("nebius", "Nebius Token Factory", "https://api.tokenfactory.nebius.com/v1", "https://tokenfactory.nebius.com"),

        // https://novita.ai/docs/guides/llm-api
        Hosted("novita", "Novita AI", "https://api.novita.ai/openai"),

        // https://docs.api.nvidia.com/nim/reference/llm-apis
        Hosted("nvidia", "Nvidia", "https://integrate.api.nvidia.com/v1"),

        // https://docs.ollama.com/api/openai-compatibility ("Direct cloud access").
        Hosted("ollama-cloud", "Ollama Cloud", "https://ollama.com/v1"),

        // https://docs.ovhcloud.com/en/guides/public-cloud/ai-machine-learning/ai-endpoints-responses-api
        Hosted("ovhcloud", "OVHcloud AI Endpoints", "https://oai.endpoints.kepler.ai.cloud.ovh.net/v1"),

        // https://docs.perplexity.ai/docs/orchestrator/quickstart (OpenAI Chat Completions, Bearer key) and
        // https://docs.perplexity.ai/api-reference/gateway-models-get (GET /models). The older https://api.perplexity.ai has no
        // model list at {address}/models, so it is not used.
        Hosted("perplexity", "Perplexity", "https://api.perplexity.ai/orchestrator/v1"),

        // https://creator.poe.com/docs/external-applications/openai-compatible-api
        Hosted("poe", "Poe", "https://api.poe.com/v1", "https://poe.com/api/keys"),

        // https://docs.requesty.ai/quickstart
        Hosted("requesty", "Requesty", "https://router.requesty.ai/v1"),

        // https://docs.sambanova.ai/docs/en/get-started/api-keys-urls
        Hosted("sambanova", "SambaNova", "https://api.sambanova.ai/v1"),

        // https://www.scaleway.com/en/docs/generative-apis/quickstart/
        Hosted("scaleway", "Scaleway", "https://api.scaleway.ai/v1"),

        // https://docs.siliconflow.com/en/userguide/quickstart
        Hosted("siliconflow", "SiliconFlow", "https://api.siliconflow.com/v1", "https://cloud.siliconflow.com/account/ak"),

        // https://docs.snowflake.com/en/user-guide/snowflake-cortex/open_ai_sdk (base_url https://<account-identifier>.snowflakecomputing.com/api/v2/cortex/v1,
        // a programmatic access token as the key). An account identifier may hold dots (the locator form) and hyphens. GET /models is not
        // documented there: unconfirmed.
        Hosted("snowflake", "Snowflake Cortex", "https://{blank}.snowflakecomputing.com/api/v2/cortex/v1", blank: new("Account", Host + "(?:\\.[A-Za-z0-9-]+)*\\z")),

        // https://dev.synthetic.new/docs/api/overview
        Hosted("synthetic", "Synthetic", "https://api.synthetic.new/openai/v1"),

        // https://docs.together.ai/docs/inference/openai-compatibility names api.together.ai; the issue gave api.together.xyz and the
        // provider's current page wins.
        Hosted("together", "Together AI", "https://api.together.ai/v1", "https://api.together.ai/settings/api-keys"),

        // https://console.upstage.ai/docs/capabilities/generate/chat and https://console.upstage.ai/api/chat
        Hosted("upstage", "Upstage", "https://api.upstage.ai/v1"),

        // https://vercel.com/docs/ai-gateway/openai-compat (GET /models, POST /chat/completions, Bearer).
        Hosted("vercel", "Vercel AI Gateway", "https://ai-gateway.vercel.sh/v1"),

        // https://api.vultrinference.com/docs/get-started/sdks
        Hosted("vultr", "Vultr", "https://api.vultrinference.com/v1"),

        // https://docs.z.ai/guides/overview/quick-start (POST /api/paas/v4/chat/completions with a Bearer key).
        Hosted("zai", "Z.AI", "https://api.z.ai/api/paas/v4"),

        // https://docs.z.ai/devpack/tool/others
        Hosted("zai-coding", "Z.AI Coding Plan", "https://api.z.ai/api/coding/paas/v4"),

        // https://lmstudio.ai/docs/developer/openai-compat (/v1/models and /v1/chat/completions).
        OnThisMachine("lmstudio", "LM Studio", "http://127.0.0.1:1234/v1", ModelProviderKeyRule.None),

        // https://docs.ollama.com/api/openai-compatibility ("Local server usage": the key is ignored).
        OnThisMachine("ollama", "Ollama", "http://localhost:11434/v1", ModelProviderKeyRule.None),

        // https://docs.vllm.ai/en/stable/getting_started/quickstart/
        OnThisMachine("vllm", "vLLM", "http://localhost:8000/v1", ModelProviderKeyRule.Optional),

        // https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md ("--port PORT ... (default: 9931)").
        OnThisMachine("llamacpp", "llama.cpp", "http://localhost:9931/v1", ModelProviderKeyRule.Optional),
    ];

    #endregion

    #region Private Methods

    private static ModelProviderPreset Hosted(string id, string name, string address, string? keyPage = null, ModelProviderAddressBlank? blank = null) =>
        new(id, name, address, ModelProviderKeyRule.Required, keyPage, Local: false, blank);

    private static ModelProviderPreset OnThisMachine(string id, string name, string address, ModelProviderKeyRule keyRule) =>
        new(id, name, address, keyRule, KeyPage: null, Local: true);

    #endregion
}
