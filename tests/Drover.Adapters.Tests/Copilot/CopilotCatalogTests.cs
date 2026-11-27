namespace Drover.Adapters.Tests;

public sealed class CopilotCatalogTests
{
    #region Parsing

    [Fact]
    public void Picker_enabled_models_that_call_tools_are_kept_and_disabled_or_tool_less_ones_are_not()
    {
        using var document = JsonDocument.Parse(
            """
            {"data":[
              {"id":"kept","model_picker_enabled":true,"policy":{"state":"enabled"},"capabilities":{"supports":{"tool_calls":true,"vision":true}}},
              {"id":"disabled","model_picker_enabled":true,"policy":{"state":"disabled"},"capabilities":{"supports":{"tool_calls":true}}},
              {"id":"no-tools","model_picker_enabled":true,"policy":{"state":"enabled"},"capabilities":{"supports":{"tool_calls":false}}}
            ]}
            """);

        var models = CopilotCatalog.Parse(document.RootElement, allowPolicyFallback: false);

        models.Should().ContainSingle().Which.Id.Should().Be("kept");
        models[0].Capabilities.Vision.Should().BeTrue();
        models[0].Capabilities.ToolCalling.Should().BeTrue();
    }

    [Fact]
    public void An_empty_picker_on_the_individual_endpoint_falls_back_to_enabled_policy()
    {
        using var document = JsonDocument.Parse(
            """
            {"data":[
              {"id":"enabled","model_picker_enabled":false,"policy":{"state":"enabled"},"capabilities":{"supports":{"tool_calls":true}}},
              {"id":"other","model_picker_enabled":false,"policy":{"state":"unconfigured"},"capabilities":{"supports":{"tool_calls":true}}}
            ]}
            """);

        CopilotCatalog.Parse(document.RootElement, allowPolicyFallback: true).Select(model => model.Id).Should().Equal("enabled");
        CopilotCatalog.Parse(document.RootElement, allowPolicyFallback: false).Should().BeEmpty();
    }

    [Fact]
    public void A_missing_data_array_is_malformed()
    {
        using var document = JsonDocument.Parse("""{"models":[]}""");

        var parsing = () => CopilotCatalog.Parse(document.RootElement, allowPolicyFallback: true);

        parsing.Should().Throw<ModelProviderException>();
    }

    #endregion
}
