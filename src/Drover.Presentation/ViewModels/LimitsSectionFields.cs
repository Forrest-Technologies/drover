namespace Drover.Presentation.ViewModels;

/// <summary>The Limits section of the bot editor: what one piece of this bot's work may use before the server stops it.</summary>
public static class LimitsSectionFields
{
    #region Properties

    public static string Caption => "These limits stop a bot before it uses too much for one piece of work.";

    public static string FallbackCaption => "Leave a limit empty to use the space's.";

    public static string StepsLabel => "Model calls";

    public static string TokensLabel => "Tokens";

    public static string DelegationsLabel => "Handoffs";

    public static string NoLimit => "No limit";

    #endregion

    #region Public Methods

    public static string StepsHint(BotRunLimitOptions? options) => options?.MaxSteps is { } most
        ? Allowed("Each time the bot asks its model what to do next.", most)
        : "Each time the bot asks its model what to do next. No limit.";

    public static string TokensHint(BotRunLimitOptions? options) => Allowed("What the bot reads and writes, added together.", options?.MaxTokens);

    public static string DelegationsHint(BotRunLimitOptions? options) =>
        Allowed("Times the bot hands work to a bot beneath it. 0 keeps all work with this bot.", options?.MaxDelegations);

    /// <summary>The space's limit, which a bot's own may only lower, as the field's placeholder. Empty until the server has said.</summary>
    public static string Placeholder(int? most) => most is BotRunLimitOptions.NoLimit ? NoLimit : BotSummaries.Count(most);

    public static string StepsPlaceholder(int? most) => most is { } value && value != BotRunLimitOptions.NoLimit ? BotSummaries.Count(value) : NoLimit;

    #endregion

    #region Private Methods

    private static string Allowed(string what, int? most) => most switch
    {
        BotRunLimitOptions.NoLimit => $"{what} This space sets no limit.",
        { } value => $"{what} This space allows up to {BotSummaries.Count(value)}.",
        _ => what,
    };

    #endregion
}
