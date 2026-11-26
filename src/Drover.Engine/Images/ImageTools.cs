namespace Drover.Engine.Images;

/// <summary>Creates images through the bot's model connection and adds them to its reply.</summary>
public static class ImageTools
{
    #region Private Fields

    public const string CreateName = "image.create";

    private const int MaxPromptCharacters = 1500;

    private const int MaxModelCharacters = 200;

    private static readonly string[] sizes = ["1024x1024", "1536x1024", "1024x1536"];

    #endregion

    #region Public Methods

    public static IServiceCollection AddImageTools(this IServiceCollection services) =>
        services
            .AddSingleton<GeneratedImageSharing>()
            .AddSingleton(provider => Create(
                provider.GetServices<IModelImageGenerator>(),
                provider.GetRequiredService<GeneratedImageSharing>(),
                provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>(),
                provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ImageTools))));

    public static bool IsImageTool(string name) => name is CreateName;

    public static BuiltinTool Create(
        IEnumerable<IModelImageGenerator> generators,
        GeneratedImageSharing sharing,
        IOptions<DeploymentPolicyOptions> deployment,
        ILogger logger) =>
        new(
            CreateName,
            "Create image",
            "Creates one PNG through an image model on this bot's model connection and adds it to this conversation.",
            JsonElement.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "prompt": {
                      "type": "string",
                      "description": "A precise image description, from 1 to 1500 characters."
                    },
                    "model": {
                      "type": "string",
                      "description": "The exact image model name offered by this bot's model connection."
                    },
                    "size": {
                      "type": "string",
                      "enum": ["1024x1024", "1536x1024", "1024x1536"],
                      "description": "The image dimensions. Defaults to 1024x1024."
                    },
                    "caption": { "type": "string", "description": "An optional short caption shown with the image." }
                  },
                  "required": ["prompt", "model"]
                }
                """),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                EnsureAllowed(deployment);
                var prompt = Prompt(call.Arguments);
                var model = Model(call.Arguments);
                var size = Size(call.Arguments);
                var caption = Text(call.Arguments, "caption");
                var file = await sharing.Share(
                    call,
                    maxBytes => Generate(
                        generators,
                        call,
                        new ModelImageRequest(prompt, model, size, maxBytes),
                        deployment.Value.Rules,
                        logger,
                        cancellationToken),
                    caption,
                    cancellationToken);

                return JsonSerializer.SerializeToElement(new
                {
                    file_id = file.Id,
                    name = file.Name,
                    media_type = file.ContentType,
                    size = file.Size,
                });
            });

    #endregion

    #region Generation

    private static async Task<ModelImageResult> Generate(
        IEnumerable<IModelImageGenerator> generators,
        BuiltinToolCall call,
        ModelImageRequest request,
        DeploymentPolicy rules,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var prepared = await call.Tenant.Transact(
            services => Prepare(services, call.Tenant.SpaceId, call.BotId, request.Model, cancellationToken),
            cancellationToken);
        if (!rules.OffersModelProvider(prepared.Connection.Kind))
        {
            throw new BuiltinToolException(ModelProviderCatalog.SwitchedOff(prepared.Connection.Kind));
        }

        var generator = generators.FirstOrDefault(candidate => candidate.Kind == prepared.Connection.Kind)
            ?? throw new BuiltinToolException("This server has no image adapter for the bot's model connection.");
        var address = generator.FixedAddress
            ?? (Uri.TryCreate(prepared.Connection.BaseUrl, UriKind.Absolute, out var parsed) ? parsed : null)
            ?? throw new BuiltinToolException("The bot's model connection has no usable address.");

        await call.BeginModelCall();

        try
        {
            using var outbound = OutboundAttempt.Begin(prepared.Policy);
            var endpoint = new ModelEndpoint(prepared.Connection.Kind, address)
            {
                Limits = new DiscoveryLimits(rules.MaxModelListBytes, rules.MaxModelsPerConnection),
            };
            var generated = await generator.Generate(
                endpoint,
                prepared.Key,
                request,
                cancellationToken);
            var pricing = generated.Usage.Cost is not null
                ? prepared.Pricing with { Currency = generated.Usage.Currency }
                : prepared.Pricing;
            await call.RecordModelUsage(
                new ToolModelUsage(
                    prepared.Connection.Id,
                    generated.Model,
                    pricing,
                    generated.Usage.InputTokens,
                    generated.Usage.OutputTokens)
                {
                    DirectCost = generated.Usage.Cost,
                    HasTokenUsage = generated.Usage is { InputTokens: > 0 } or { OutputTokens: > 0 },
                });
            return generated;
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning(
                "Image generation failed through connection {ConnectionId}: {Failure}",
                prepared.Connection.Id,
                failure.Failure);
            throw new BuiltinToolException($"The image could not be created. {failure.Message}");
        }
    }

    private static async Task<Prepared> Prepare(
        IServiceProvider services,
        Guid spaceId,
        Guid botId,
        string model,
        CancellationToken cancellationToken)
    {
        var bot = await services.GetRequiredService<BotRepository>().FindBot(botId, cancellationToken)
            ?? throw new BuiltinToolException("This bot no longer exists.");
        var connections = services.GetRequiredService<ModelConnectionRepository>();
        var connection = await connections.FindConnection(bot.ModelConnectionId, cancellationToken)
            ?? throw new BuiltinToolException("This bot's model connection no longer exists.");
        var catalog = await connections.ListCatalog(connection.Id, cancellationToken);
        var entry = catalog.SingleOrDefault(candidate =>
            candidate.Model == model
            && candidate.State == AvailabilityState.Available
            && candidate.Capabilities.ImageGeneration);

        if (entry is null)
        {
            var allowed = catalog
                .Where(candidate => candidate.State == AvailabilityState.Available && candidate.Capabilities.ImageGeneration)
                .Select(candidate => candidate.Model)
                .ToArray();
            throw new BuiltinToolException(
                allowed.Length == 0
                    ? "This bot's model connection has no enabled image models in its catalog."
                    : $"That image model is not enabled in this connection's catalog. Allowed image models: {string.Join(", ", allowed)}.");
        }

        var key = connection.SecretId is { } secretId
            ? await services.GetRequiredService<ISecretVault>().Reveal(secretId, cancellationToken)
            : null;
        var policy = await services.GetRequiredService<PolicyService>().GetEffective(spaceId, cancellationToken);
        return new Prepared(connection, key, entry.Pricing, policy);
    }

    #endregion

    #region Arguments

    private static void EnsureAllowed(IOptions<DeploymentPolicyOptions> deployment)
    {
        if (!deployment.Value.Rules.AllowImageGeneration)
        {
            throw new BuiltinToolException(
                $"This server does not offer image generation ({nameof(DeploymentPolicy.AllowImageGeneration)}).");
        }
    }

    private static string Prompt(JsonElement arguments) =>
        Text(arguments, "prompt") is { Length: > 0 and <= MaxPromptCharacters } prompt
            ? prompt
            : throw new BuiltinToolException(
                $"The argument prompt is required and holds at most {MaxPromptCharacters} characters.");

    private static string Model(JsonElement arguments)
    {
        var model = Text(arguments, "model");

        if (model is not { Length: > 0 and <= MaxModelCharacters })
        {
            throw new BuiltinToolException(
                $"The argument model is required and holds at most {MaxModelCharacters} characters.");
        }

        return model;
    }

    private static string Size(JsonElement arguments)
    {
        var size = Text(arguments, "size") ?? sizes[0];
        return sizes.Contains(size, StringComparer.Ordinal)
            ? size
            : throw new BuiltinToolException($"The argument size is one of: {string.Join(", ", sizes)}.");
    }

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind is JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    #endregion

    #region Nested Types

    private sealed record Prepared(
        ModelConnection Connection,
        string? Key,
        ModelPricing Pricing,
        EffectivePolicy Policy);

    #endregion
}
