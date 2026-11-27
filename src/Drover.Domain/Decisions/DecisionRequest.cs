namespace Drover.Domain.Decisions;

/// <summary>
/// What a decision model is asked: a state made of named pieces of text, and the questions to answer about it. Each
/// question is answered on its own.
/// </summary>
public sealed record DecisionRequest(IReadOnlyDictionary<string, string> State, IReadOnlyList<DecisionQuestion> Questions);

/// <summary>
/// One question. With no choices it is a yes or no question; with choices, the model picks one of them. The name is
/// how its answer is found.
/// </summary>
public sealed record DecisionQuestion(string Name, string Text)
{
    #region Properties

    public IReadOnlyList<string> Choices { get; init; } = [];

    #endregion
}

/// <summary>The answers, by question name, and the input tokens the request used.</summary>
public sealed record DecisionAnswer(IReadOnlyDictionary<string, DecisionResult> Answers, long InputTokens);

/// <summary>
/// The answer to one question: the probability of yes for a yes or no question, or the probability of each choice
/// for a question with choices.
/// </summary>
public sealed record DecisionResult
{
    #region Properties

    public double? Yes { get; init; }

    public IReadOnlyDictionary<string, double> Choices { get; init; } = new Dictionary<string, double>();

    #endregion
}
