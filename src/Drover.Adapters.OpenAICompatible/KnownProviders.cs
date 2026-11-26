namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The named providers a person can pick instead of typing an address. This is the one place an address is written
/// down: the table is compiled in and reviewed, and nothing at run time adds or changes a row. A connection made from
/// a row is stored as an ordinary compatible connection, so every check on a hand-typed address applies to it.
///
/// The order is the order a person sees: a short lead of the best-known providers, then the other hosted providers
/// by name, then the ones on the person's own machine or network.
///
/// Each row names the page that confirmed its address on 2026-11-26. A row marked "unconfirmed" ships with the value
/// the issue gave: its provider's page could not be read or did not show the address, and the owner's live run
/// (<c>KnownProvidersLiveTests</c>) is what settles it. Key pages were not fetched and are the providers' own
/// well-known console addresses. Left out because their documentation says so: Perplexity (the model list is at
/// <c>/v1/models</c> and chat has moved to its own agent endpoint, so <c>{address}/models</c> does not hold),
/// GitHub Models (retired on 2026-07-30, https://docs.github.com/en/rest/models/inference).
/// </summary>
public static class KnownProviders
{
    #region Rows

    /// <summary>Every row, in the order offered.</summary>
    public static IReadOnlyList<ModelProviderPreset> All { get; } =
    [
        // Unconfirmed: the page was not read. The address is the one in docs/architecture/15-model-providers.md.
        Hosted("openai", "OpenAI", "https://api.openai.com/v1", "https://platform.openai.com/api-keys"),

        // https://openrouter.ai/docs/quickstart serves its API under /api/v1. Chat completions path unconfirmed on the page.
        Hosted("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "https://openrouter.ai/settings/keys"),

        // https://ai.google.dev/gemini-api/docs/openai. #529 removes this row when its own kind lands.
        Hosted("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", "https://aistudio.google.com/apikey"),

        // Unconfirmed: the page read (https://docs.x.ai/docs/api-reference) leads with the Responses API. The address is the
        // one in docs/architecture/15-model-providers.md.
        Hosted("xai", "xAI", "https://api.x.ai/v1", "https://console.x.ai"),

        // https://console.groq.com/docs/openai
        Hosted("groq", "Groq", "https://api.groq.com/openai/v1", "https://console.groq.com/keys"),

        // https://api-docs.deepseek.com/ (base_url for the OpenAI format).
        Hosted("deepseek", "DeepSeek", "https://api.deepseek.com", "https://platform.deepseek.com/api_keys"),

        // https://docs.mistral.ai/api/endpoint/chat confirms POST /v1/chat/completions. Host unconfirmed on the page.
        Hosted("mistral", "Mistral", "https://api.mistral.ai/v1", "https://console.mistral.ai/api-keys"),

        // Unconfirmed: the page could not be read.
        Hosted("alibaba", "Alibaba", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("baseten", "Baseten", "https://inference.baseten.co/v1"),

        // https://inference-docs.cerebras.ai/resources/openai
        Hosted("cerebras", "Cerebras", "https://api.cerebras.ai/v1", "https://cloud.cerebras.ai"),

        // Unconfirmed: the page could not be read.
        Hosted("chutes", "Chutes", "https://llm.chutes.ai/v1"),

        // https://docs.cohere.com/docs/compatibility-api
        Hosted("cohere", "Cohere", "https://api.cohere.ai/compatibility/v1", "https://dashboard.cohere.com/api-keys"),

        // https://deepinfra.com/docs/openai_api
        Hosted("deepinfra", "Deep Infra", "https://api.deepinfra.com/v1/openai"),

        // Unconfirmed: the page could not be read.
        Hosted("digitalocean", "DigitalOcean", "https://inference.do-ai.run/v1"),

        // https://docs.fireworks.ai/tools-sdks/openai-compatibility (written without the trailing slash there).
        Hosted("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("friendli", "Friendli", "https://api.friendli.ai/serverless/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("helicone", "Helicone", "https://ai-gateway.helicone.ai/v1"),

        // Unconfirmed: https://huggingface.co/docs/inference-providers/index was read and names the router but not the address.
        Hosted("huggingface", "Hugging Face", "https://router.huggingface.co/v1", "https://huggingface.co/settings/tokens"),

        // Unconfirmed: the page could not be read.
        Hosted("inception", "Inception", "https://api.inceptionlabs.ai/v1/"),

        // https://platform.moonshot.ai/docs/guide/start-using-kimi-api (base_url in the OpenAI example).
        Hosted("moonshot", "Moonshot AI", "https://api.moonshot.ai/v1"),

        // https://docs.tokenfactory.nebius.com/quickstart
        Hosted("nebius", "Nebius Token Factory", "https://api.tokenfactory.nebius.com/v1", "https://tokenfactory.nebius.com"),

        // Unconfirmed: the page could not be read.
        Hosted("novita", "Novita AI", "https://api.novita.ai/openai"),

        // Unconfirmed: the page could not be read.
        Hosted("nvidia", "Nvidia", "https://integrate.api.nvidia.com/v1"),

        // https://docs.ollama.com/api/openai-compatibility ("Direct cloud access").
        Hosted("ollama-cloud", "Ollama Cloud", "https://ollama.com/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("ovhcloud", "OVHcloud AI Endpoints", "https://oai.endpoints.kepler.ai.cloud.ovh.net/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("poe", "Poe", "https://api.poe.com/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("requesty", "Requesty", "https://router.requesty.ai/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("sambanova", "SambaNova", "https://api.sambanova.ai/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("scaleway", "Scaleway", "https://api.scaleway.ai/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("siliconflow", "SiliconFlow", "https://api.siliconflow.com/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("synthetic", "Synthetic", "https://api.synthetic.new/openai/v1"),

        // https://docs.together.ai/docs/openai-api-compatibility names https://api.together.ai/v1. The issue gave
        // api.together.xyz; the provider's current page wins.
        Hosted("together", "Together AI", "https://api.together.ai/v1", "https://api.together.ai/settings/api-keys"),

        // Unconfirmed: the page could not be read.
        Hosted("upstage", "Upstage", "https://api.upstage.ai/v1/solar"),

        // https://vercel.com/docs/ai-gateway/openai-compat (GET /models, POST /chat/completions, Bearer).
        Hosted("vercel", "Vercel AI Gateway", "https://ai-gateway.vercel.sh/v1"),

        // Unconfirmed: the page could not be read.
        Hosted("vultr", "Vultr", "https://api.vultrinference.com/v1"),

        // https://docs.z.ai/guides/overview/quick-start (POST /api/paas/v4/chat/completions with a Bearer key).
        Hosted("zai", "Z.AI", "https://api.z.ai/api/paas/v4"),

        // Unconfirmed: https://docs.z.ai/devpack/tool/others was read and does not show the address.
        Hosted("zai-coding", "Z.AI Coding Plan", "https://api.z.ai/api/coding/paas/v4"),

        // https://lmstudio.ai/docs/developer/openai-compat (/v1/models and /v1/chat/completions). The loopback spelling is the issue's.
        OnThisMachine("lmstudio", "LM Studio", "http://127.0.0.1:1234/v1", ModelProviderKeyRule.None),

        // https://docs.ollama.com/api/openai-compatibility ("Local server usage": the key is ignored).
        OnThisMachine("ollama", "Ollama", "http://localhost:11434/v1", ModelProviderKeyRule.None),

        // Unconfirmed: the page was not read. The address is the one in the issue.
        OnThisMachine("vllm", "vLLM", "http://localhost:8000/v1", ModelProviderKeyRule.Optional),

        // Unconfirmed: the page was not read. The address is the one in the issue.
        OnThisMachine("llamacpp", "llama.cpp", "http://localhost:8080/v1", ModelProviderKeyRule.Optional),
    ];

    #endregion

    #region Private Methods

    private static ModelProviderPreset Hosted(string id, string name, string address, string? keyPage = null) =>
        new(id, name, address, ModelProviderKeyRule.Required, keyPage, Local: false);

    private static ModelProviderPreset OnThisMachine(string id, string name, string address, ModelProviderKeyRule keyRule) =>
        new(id, name, address, keyRule, KeyPage: null, Local: true);

    #endregion
}
