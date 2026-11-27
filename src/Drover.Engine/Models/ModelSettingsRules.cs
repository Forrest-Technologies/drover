namespace Drover.Engine.Models;

/// <summary>What an admin may say about a model, and the sentence for each way it can be wrong.</summary>
public static class ModelSettingsRules
{
    #region Private Fields

    public const string SizeProblem = "Enter a size of 1 token or more.";

    public const string PriceProblem = "Enter a price of zero or more per million tokens.";

    public const string CurrencyProblem = "Enter a three letter currency code, such as USD.";

    public const string EffortsNeedReasoningProblem = "Choose effort levels only for a model that thinks before answering.";

    public const string EffortProblem = "Choose effort levels from minimal, low, medium, high, xhigh and max.";

    public const int MaxPriceDecimals = 6;

    private static readonly string[] efforts = ["minimal", "low", "medium", "high", "xhigh", "max"];

    #endregion

    #region Public Methods

    /// <summary>The sentence for the first thing wrong with the abilities, or null when they can be stored.</summary>
    public static string? FindProblem(ModelCapabilityOverrides abilities, bool reasoningInEffect)
    {
        if (abilities.ContextTokens is < 1 || abilities.MaxOutputTokens is < 1)
        {
            return SizeProblem;
        }

        if (abilities.ReasoningEfforts is not { Count: > 0 } requested)
        {
            return null;
        }

        if (requested.Any(effort => !efforts.Contains(effort, StringComparer.Ordinal)))
        {
            return EffortProblem;
        }

        return reasoningInEffect ? null : EffortsNeedReasoningProblem;
    }

    /// <summary>The sentence for the first thing wrong with the prices, or null when they can be stored.</summary>
    public static string? FindProblem(ModelPricing prices, string? currency)
    {
        if (!IsPrice(prices.InputPerMillion) || !IsPrice(prices.OutputPerMillion) || !IsPrice(prices.CachedInputPerMillion))
        {
            return PriceProblem;
        }

        return currency is null || IsCurrency(currency) ? null : CurrencyProblem;
    }

    /// <summary>
    /// The abilities as they are stored: efforts in lower case, an empty list meaning none set, and null when no
    /// member is set at all.
    /// </summary>
    public static ModelCapabilityOverrides? Normalize(ModelCapabilityOverrides abilities)
    {
        var tidy = abilities with
        {
            ReasoningEfforts = abilities.ReasoningEfforts is { Count: > 0 } requested ? [.. requested.Distinct()] : null,
        };

        return tidy.IsEmpty ? null : tidy;
    }

    #endregion

    #region Private Methods

    private static bool IsPrice(decimal? price) =>
        price is null || (price >= 0 && decimal.Round(price.Value, MaxPriceDecimals) == price);

    private static bool IsCurrency(string currency) =>
        currency.Length == FieldLimits.CurrencyCode && currency.All(char.IsAsciiLetter);

    #endregion
}
