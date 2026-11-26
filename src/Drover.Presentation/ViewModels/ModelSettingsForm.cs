namespace Drover.Presentation.ViewModels;

/// <summary>
/// The sheet where an admin says what one model can do and costs. Its job: show what Drover knows about the model,
/// whether the provider said it or an admin did, and let the admin fill in what is wrong or missing. The form opens
/// filled with the effective values, so a member is sent only when the admin changed it or had already set it:
/// everything else stays the provider's.
/// </summary>
public sealed class ModelSettingsForm
{
    #region Private Fields

    public const string SizeProblem = "Enter a size of 1 token or more.";

    public const string PriceProblem = "Enter a price of zero or more per million tokens.";

    public const string CurrencyProblem = "Enter a three letter currency code, such as USD.";

    public const string FromProvider = "From the provider";

    public const string SetHere = "Set here";

    public const string UseProviderTitle = "Use the provider's values";

    public const string KeepThem = "Keep them";

    private const int MaxPriceDecimals = 6;

    private readonly Dictionary<string, string> problems = [];

    private ModelCatalogResponse? opened;

    #endregion

    #region Properties

    /// <summary>The effort levels a model can take, in the order a person thinks of them.</summary>
    public static IReadOnlyList<string> EffortLevels { get; } = ["minimal", "low", "medium", "high", "xhigh", "max"];

    public ModelCatalogResponse? Entry => opened;

    public string ContextSize { get; set; } = string.Empty;

    public string MaxOutputSize { get; set; } = string.Empty;

    public bool ToolCalling { get; set; }

    public bool Vision { get; set; }

    public bool Reasoning { get; set; }

    public HashSet<string> Efforts { get; } = [];

    public bool StructuredOutput { get; set; }

    public bool ImageGeneration { get; set; }

    public string InputPrice { get; set; } = string.Empty;

    public string OutputPrice { get; set; } = string.Empty;

    public string CachedPrice { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";

    /// <summary>The words under the sheet's title.</summary>
    public string Intro => "What Drover knows about this model. Fill in what the provider did not report.";

    /// <summary>Effort levels make sense only for a model that thinks before answering.</summary>
    public bool ShowsEfforts => Reasoning;

    /// <summary>The button that clears everything set here is offered only when something is.</summary>
    public bool OffersUseProvidersValues => opened?.OperatorSet is { Count: > 0 };

    public string UseProvidersValuesBody => $"What was set here for {opened?.DisplayName} is removed.";

    public IReadOnlyDictionary<string, string> Problems => problems;

    #endregion

    #region Public Methods

    /// <summary>Fills the form with what the model can do and costs now, wherever each value came from.</summary>
    public void Open(ModelCatalogResponse entry)
    {
        opened = entry;
        problems.Clear();
        var can = entry.Capabilities;
        ContextSize = can.ContextSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        MaxOutputSize = can.MaxOutputSize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ToolCalling = can.ToolCalling;
        Vision = can.Vision;
        Reasoning = can.Reasoning;
        Efforts.Clear();
        Efforts.UnionWith(can.ReasoningEfforts);
        StructuredOutput = can.StructuredOutput;
        ImageGeneration = can.ImageGeneration;
        InputPrice = Show(entry.Price.InputPerMillion);
        OutputPrice = Show(entry.Price.OutputPerMillion);
        CachedPrice = Show(entry.Price.CachedInputPerMillion);
        Currency = entry.Price.Currency.ToUpperInvariant();
    }

    /// <summary>
    /// Says where a value came from, under its field. A value an admin set says so. A value the provider reported says
    /// so, when it is one the provider is known to have reported: a switch that is off, and tool calling, which every
    /// model is assumed to have, say nothing.
    /// </summary>
    public string? Caption(string member)
    {
        if (opened is null)
        {
            return null;
        }

        if (opened.OperatorSet?.Contains(member) is true)
        {
            return SetHere;
        }

        return Reported(member) ? FromProvider : null;
    }

    /// <summary>Checks every value. A sentence is kept for each field that is wrong, and false is returned.</summary>
    public bool Check()
    {
        problems.Clear();
        CheckSize(nameof(ContextSize), ContextSize);
        CheckSize(nameof(MaxOutputSize), MaxOutputSize);
        CheckPrice(nameof(InputPrice), InputPrice);
        CheckPrice(nameof(OutputPrice), OutputPrice);
        CheckPrice(nameof(CachedPrice), CachedPrice);

        if (!IsCurrency(Currency))
        {
            problems[nameof(Currency)] = CurrencyProblem;
        }

        return problems.Count == 0;
    }

    /// <summary>
    /// What to send: each member the admin changed or had already set, and nothing else. A member left out is cleared by
    /// the server, so the provider's value is in effect for it.
    /// </summary>
    public SetModelSettingsRequest Request()
    {
        var entry = opened ?? throw new InvalidOperationException("The form is not open.");
        var can = entry.Capabilities;
        var abilities = new ModelAbilitiesRequest(
            ContextSize: SizeToSend("contextSize", ContextSize, can.ContextSize),
            MaxOutputSize: SizeToSend("maxOutputSize", MaxOutputSize, can.MaxOutputSize),
            ToolCalling: FlagToSend("toolCalling", ToolCalling, can.ToolCalling),
            Vision: FlagToSend("vision", Vision, can.Vision),
            Reasoning: FlagToSend("reasoning", Reasoning, can.Reasoning),
            ReasoningEfforts: EffortsToSend(can),
            StructuredOutput: FlagToSend("structuredOutput", StructuredOutput, can.StructuredOutput),
            ImageGeneration: FlagToSend("imageGeneration", ImageGeneration, can.ImageGeneration));
        var prices = entry.Price;
        var priced = new ModelPricingRequest(
            InputPerMillion: PriceToSend("inputPerMillion", InputPrice, prices.InputPerMillion),
            OutputPerMillion: PriceToSend("outputPerMillion", OutputPrice, prices.OutputPerMillion),
            CachedInputPerMillion: PriceToSend("cachedInputPerMillion", CachedPrice, prices.CachedInputPerMillion),
            Currency: Currency.Trim().ToUpperInvariant());

        return new SetModelSettingsRequest(
            entry.Model,
            abilities,
            priced is { InputPerMillion: null, OutputPerMillion: null, CachedInputPerMillion: null } ? null : priced);
    }

    #endregion

    #region Private Methods

    private bool Reported(string member)
    {
        var can = opened!.Capabilities;
        var price = opened.Price;

        return member switch
        {
            "contextSize" => can.ContextSize is not null,
            "maxOutputSize" => can.MaxOutputSize is not null,
            "vision" => can.Vision,
            "reasoning" => can.Reasoning,
            "reasoningEfforts" => can.ReasoningEfforts.Count > 0,
            "structuredOutput" => can.StructuredOutput,
            "imageGeneration" => can.ImageGeneration,
            "inputPerMillion" => price.InputPerMillion is not null,
            "outputPerMillion" => price.OutputPerMillion is not null,
            "cachedInputPerMillion" => price.CachedInputPerMillion is not null,
            _ => false,
        };
    }

    private bool IsSet(string member) => opened?.OperatorSet?.Contains(member) is true;

    private int? SizeToSend(string member, string typed, int? shown)
    {
        var size = ParseSize(typed);

        return size is not null && (IsSet(member) || size != shown) ? size : null;
    }

    private decimal? PriceToSend(string member, string typed, decimal? shown)
    {
        var price = ParsePrice(typed);

        return price is not null && (IsSet(member) || price != shown) ? price : null;
    }

    private bool? FlagToSend(string member, bool now, bool shown) => IsSet(member) || now != shown ? now : null;

    private List<string>? EffortsToSend(ModelCapabilitiesResponse can)
    {
        if (!Reasoning || Efforts.Count == 0)
        {
            return null;
        }

        var chosen = EffortLevels.Where(Efforts.Contains).ToList();

        return IsSet("reasoningEfforts") || !chosen.SequenceEqual(can.ReasoningEfforts) ? chosen : null;
    }

    private void CheckSize(string field, string typed)
    {
        if (typed.Trim().Length > 0 && ParseSize(typed) is null)
        {
            problems[field] = SizeProblem;
        }
    }

    private void CheckPrice(string field, string typed)
    {
        if (typed.Trim().Length > 0 && ParsePrice(typed) is null)
        {
            problems[field] = PriceProblem;
        }
    }

    private static int? ParseSize(string typed) =>
        int.TryParse(typed.Trim(), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var size) && size >= 1 ? size : null;

    private static decimal? ParsePrice(string typed) =>
        decimal.TryParse(typed.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
        && price >= 0
        && decimal.Round(price, MaxPriceDecimals) == price
            ? price
            : null;

    private static bool IsCurrency(string typed) => typed.Trim() is { Length: Drover.Contracts.FieldLimits.CurrencyCode } code && code.All(char.IsAsciiLetter);

    private static string Show(decimal? price) => price?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;

    #endregion
}
