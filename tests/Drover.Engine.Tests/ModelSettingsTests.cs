namespace Drover.Engine.Tests;

/// <summary>What an admin may say about a model, and how it is laid over what the provider reported.</summary>
public sealed class ModelSettingsTests
{
    #region Validation

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_size_below_one_is_refused_with_its_sentence(int size)
    {
        ModelSettingsRules.FindProblem(new ModelCapabilityOverrides { ContextTokens = size }, false).Should().Be("Enter a size of 1 token or more.");
        ModelSettingsRules.FindProblem(new ModelCapabilityOverrides { MaxOutputTokens = size }, false).Should().Be("Enter a size of 1 token or more.");
    }

    [Fact]
    public void A_size_above_ten_million_is_accepted()
    {
        ModelSettingsRules.FindProblem(new ModelCapabilityOverrides { ContextTokens = 2_000_000_000 }, false).Should().BeNull();
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.0000001")]
    public void A_price_below_zero_or_with_more_than_six_decimals_is_refused(string price)
    {
        var prices = new ModelPricing { OutputPerMillion = decimal.Parse(price, CultureInfo.InvariantCulture) };

        ModelSettingsRules.FindProblem(prices, null).Should().Be("Enter a price of zero or more per million tokens.");
    }

    [Fact]
    public void A_price_of_zero_with_six_decimals_is_accepted()
    {
        ModelSettingsRules.FindProblem(new ModelPricing { InputPerMillion = 0m, CachedInputPerMillion = 0.123456m }, "usd").Should().BeNull();
    }

    [Theory]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("DOLLARS")]
    public void A_currency_that_is_not_three_letters_is_refused(string currency)
    {
        ModelSettingsRules.FindProblem(new ModelPricing(), currency).Should().Be("Enter a three letter currency code, such as USD.");
    }

    [Fact]
    public void Efforts_come_from_the_known_levels_and_only_where_the_model_thinks()
    {
        var efforts = new ModelCapabilityOverrides { ReasoningEfforts = ["low", "max"] };

        ModelSettingsRules.FindProblem(efforts, reasoningInEffect: true).Should().BeNull();
        ModelSettingsRules.FindProblem(efforts, reasoningInEffect: false).Should().NotBeNull();
        ModelSettingsRules.FindProblem(new ModelCapabilityOverrides { ReasoningEfforts = ["extreme"] }, true).Should().NotBeNull();
    }

    [Fact]
    public void Nothing_set_is_stored_as_null_and_an_empty_effort_list_as_not_set()
    {
        ModelSettingsRules.Normalize(new ModelCapabilityOverrides { ReasoningEfforts = [] }).Should().BeNull();
        ModelSettingsRules.Normalize(new ModelCapabilityOverrides { Vision = false }).Should().Be(new ModelCapabilityOverrides { Vision = false });
    }

    #endregion

    #region Effective Values

    [Fact]
    public void An_unset_member_takes_the_providers_value_and_a_set_one_the_admins_even_when_false()
    {
        var entry = new ModelCatalogEntry
        {
            Model = "m",
            DisplayName = "M",
            Capabilities = new ModelCapabilities { ContextTokens = 8192, Vision = true, ToolCalling = true },
            OperatorCapabilities = new ModelCapabilityOverrides { Vision = false, MaxOutputTokens = 2048 },
        };

        entry.EffectiveCapabilities.Should().Match<ModelCapabilities>(can =>
            can.ContextTokens == 8192 && !can.Vision && can.ToolCalling && can.MaxOutputTokens == 2048);
    }

    [Fact]
    public void Turning_thinking_off_drops_the_providers_effort_levels()
    {
        var entry = new ModelCatalogEntry
        {
            Model = "m",
            DisplayName = "M",
            Capabilities = new ModelCapabilities { Reasoning = true, ReasoningEfforts = ["low", "high"] },
            OperatorCapabilities = new ModelCapabilityOverrides { Reasoning = false },
        };

        entry.EffectiveCapabilities.ReasoningEfforts.Should().BeEmpty();
    }

    [Fact]
    public void Prices_are_laid_over_one_by_one_and_the_currency_is_the_admins_once_any_price_is_set()
    {
        var entry = new ModelCatalogEntry
        {
            Model = "m",
            DisplayName = "M",
            Pricing = new ModelPricing { InputPerMillion = 3m, OutputPerMillion = 15m },
            OperatorPricing = new ModelPricing { OutputPerMillion = 1m, Currency = "EUR" },
        };

        entry.EffectivePricing.Should().Be(new ModelPricing { InputPerMillion = 3m, OutputPerMillion = 1m, Currency = "EUR" });
    }

    [Fact]
    public void A_model_nobody_set_anything_on_is_read_as_the_provider_reported_it()
    {
        var entry = new ModelCatalogEntry { Model = "m", DisplayName = "M", Capabilities = new ModelCapabilities { ContextTokens = 1000 } };

        entry.EffectiveCapabilities.Should().BeSameAs(entry.Capabilities);
        entry.EffectivePricing.Should().BeSameAs(entry.Pricing);
    }

    #endregion
}
