namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Asks a server for what it knows about each listed model with <c>POST /api/show</c>, the call of a local server
/// that keeps a model's abilities and context length where its plain model lists carry neither:
/// https://docs.ollama.com/api-reference/show-model-details. The first model is the probe. A server that answers
/// it with anything but a 200 holding a <c>capabilities</c> list is not that kind of server, and probing ends there
/// with no other request.
/// </summary>
internal static class OllamaProbe
{
    #region Private Fields

    /// <summary>The whole pass fits inside the time the model list call itself is given.</summary>
    private static readonly TimeSpan budget = TimeSpan.FromMinutes(2);

    private const int AtOnce = 4;

    #endregion

    #region Public Methods

    /// <summary>
    /// The models with what the server said about each. A model the pass did not reach keeps what the list gave and
    /// is marked as not read, so a stored value is kept and the next pass asks for the models not yet filled first.
    /// </summary>
    public static async Task<List<ModelInfo>> Fill(
        HttpClient client,
        ModelEndpoint connection,
        string? apiKey,
        List<ModelInfo> models,
        CancellationToken cancellation)
    {
        if (models.Count == 0)
        {
            return models;
        }

        var ordered = models.OrderBy(model => connection.Filled.Contains(model.Id)).ToList();
        var origin = new Uri(connection.BaseAddress.GetLeftPart(UriPartial.Authority));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(budget);

        var first = (await Show(client, origin, connection, apiKey, ordered[0], limit.Token, cancellation)).Model;

        if (first is null)
        {
            return models;
        }

        ConcurrentDictionary<string, ModelInfo> filled = new(StringComparer.Ordinal) { [first.Id] = first };

        await Parallel.ForEachAsync(
            ordered.Skip(1),
            new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = cancellation },
            async (model, token) =>
            {
                // Once the pass is out of time no new model is asked for; the ones in flight are cut off by the limit.
                if (!limit.IsCancellationRequested
                    && (await Show(client, origin, connection, apiKey, model, limit.Token, cancellation)).Model is { } read)
                {
                    filled[read.Id] = read;
                }
            });

        return [.. models.Select(model => filled.TryGetValue(model.Id, out var read) ? read : model with { FactsRead = false })];
    }

    #endregion

    #region Private Methods

    /// <summary>The model with its facts, or no model and what went wrong when the server did not answer as a local server does.</summary>
    private static async Task<Shown> Show(
        HttpClient client,
        Uri origin,
        ModelEndpoint connection,
        string? apiKey,
        ModelInfo model,
        CancellationToken limit,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "/api/show"))
        {
            Content = JsonContent.Create(new { model = model.Id }),
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new Shown(null, $"status {(int)response.StatusCode}");
            }

            using var document = await BoundedResponse.ReadJson(response.Content, connection.Limits.MaxBytes, "model details", limit);

            var read = Read(model, document.RootElement);

            return new Shown(read, read is null ? "no capabilities" : null);
        }
        catch (Exception problem) when (IsAnswerNotUsable(problem, cancellation))
        {
            // An answer too large or not JSON, or a pass out of time: this model keeps what the list gave. A call
            // the person cancelled is not caught here.
            return new Shown(null, problem.GetType().Name);
        }
    }

    private static bool IsAnswerNotUsable(Exception problem, CancellationToken cancellation) =>
        problem is HttpRequestException or JsonException or InvalidOperationException or ModelProviderException
        || (problem is OperationCanceledException && !cancellation.IsCancellationRequested);

    private static ModelInfo? Read(ModelInfo model, JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object
            || !answer.TryGetProperty("capabilities", out var listed)
            || listed.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var abilities = listed.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).ToHashSet();

        return model with
        {
            Capabilities = model.Capabilities with
            {
                ToolCalling = abilities.Contains("tools"),
                Vision = abilities.Contains("vision"),
                Reasoning = abilities.Contains("thinking"),
                ContextTokens = ReadContextLength(answer) ?? model.Capabilities.ContextTokens,
            },
        };
    }

    /// <summary>The <c>model_info</c> key that ends in <c>.context_length</c>: its front is the model's family.</summary>
    private static int? ReadContextLength(JsonElement answer)
    {
        if (!answer.TryGetProperty("model_info", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in info.EnumerateObject())
        {
            if (property.Name.EndsWith(".context_length", StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var length)
                && length > 0)
            {
                return length;
            }
        }

        return null;
    }

    #endregion

    #region Nested Types

    private sealed record Shown(ModelInfo? Model, string? Why);

    #endregion
}
