namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Tightens a tool JSON schema so Copilot's backends, which reject open objects, will take it. Missing
/// <c>additionalProperties</c> on an object becomes false; nested objects and array items are treated the same.
/// </summary>
internal static class CopilotToolSchema
{
    #region Public Methods

    public static JsonElement Tighten(JsonElement schema)
    {
        if (schema.ValueKind is not JsonValueKind.Object)
        {
            return schema;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteObject(writer, schema);
        }

        return JsonSerializer.Deserialize<JsonElement>(stream.ToArray());
    }

    #endregion

    #region Private Methods

    private static void WriteObject(Utf8JsonWriter writer, JsonElement source)
    {
        writer.WriteStartObject();
        var type = source.TryGetProperty("type", out var declared) ? declared.GetString() : null;
        var wroteAdditional = false;

        foreach (var property in source.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);

            if (property.NameEquals("additionalProperties"))
            {
                wroteAdditional = true;
                property.Value.WriteTo(writer);
            }
            else if (property.NameEquals("properties") && property.Value.ValueKind is JsonValueKind.Object)
            {
                WriteProperties(writer, property.Value);
            }
            else if (property.NameEquals("items") && property.Value.ValueKind is JsonValueKind.Object)
            {
                WriteObject(writer, property.Value);
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }

        if (type is "object" && !wroteAdditional)
        {
            writer.WriteBoolean("additionalProperties", false);
        }

        writer.WriteEndObject();
    }

    private static void WriteProperties(Utf8JsonWriter writer, JsonElement properties)
    {
        writer.WriteStartObject();

        foreach (var property in properties.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);

            if (property.Value.ValueKind is JsonValueKind.Object)
            {
                WriteObject(writer, property.Value);
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    #endregion
}
