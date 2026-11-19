namespace Drover.Adapters.Venice;

/// <summary>Verifies an image model and reads only its unambiguous flat USD generation price.</summary>
internal static class VeniceImageCatalog
{
    #region Public Methods

    public static VeniceImageModel? Find(JsonElement root, string modelId)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException("The image model catalog has no model list.");
        }

        foreach (var model in data.EnumerateArray())
        {
            if (ReadString(model, "id") == modelId && !IsOffline(model))
            {
                return new VeniceImageModel(ReadFlatUsd(model));
            }
        }

        return null;
    }

    #endregion

    #region Private Methods

    private static decimal? ReadFlatUsd(JsonElement model)
    {
        if (!model.TryGetProperty("model_spec", out var spec)
            || !spec.TryGetProperty("pricing", out var pricing)
            || pricing.ValueKind is not JsonValueKind.Object
            || pricing.TryGetProperty("resolutions", out _)
            || pricing.TryGetProperty("quality", out _)
            || !pricing.TryGetProperty("generation", out var generation)
            || generation.ValueKind is not JsonValueKind.Object
            || !generation.TryGetProperty("usd", out var usd)
            || !usd.TryGetDecimal(out var price))
        {
            return null;
        }

        return price >= 0 ? price : null;
    }

    private static bool IsOffline(JsonElement model) =>
        model.TryGetProperty("model_spec", out var spec)
        && spec.TryGetProperty("offline", out var offline)
        && offline.ValueKind is JsonValueKind.True;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    #endregion
}


/// <summary>An image model verified in the current provider catalog and its optional flat USD price.</summary>
internal sealed record VeniceImageModel(decimal? FlatUsd);
