namespace Drover.Adapters.Tests;

public sealed class CopilotHeadersTests
{
    #region Schema

    [Fact]
    public void A_tool_object_without_additional_properties_is_closed()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""");

        var tightened = CopilotToolSchema.Tighten(schema.RootElement);

        tightened.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        tightened.GetProperty("properties").GetProperty("city").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void Nested_objects_and_array_items_are_closed_too()
    {
        using var schema = JsonDocument.Parse(
            """
            {"type":"object","properties":{"place":{"type":"object","properties":{"city":{"type":"string"}}},"tags":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}}}}
            """);

        var tightened = CopilotToolSchema.Tighten(schema.RootElement);

        tightened.GetProperty("properties").GetProperty("place").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        tightened.GetProperty("properties").GetProperty("tags").GetProperty("items").GetProperty("additionalProperties")
            .GetBoolean().Should().BeFalse();
    }

    #endregion
}
