namespace Drover.Domain.Decisions;

/// <summary>
/// A model that answers typed questions about a state with probabilities, and writes no text. Drover asks one to
/// judge a rule written as a sentence; the answer is compared with the rule's own numbers in code. The model is
/// never the bot's model, is never given a tool, and its answer can only narrow what a bot may do.
/// Provider failures surface as <see cref="ModelProviderException"/>.
/// </summary>
public interface IDecisionModel
{
    Task<DecisionAnswer> Decide(DecisionRequest request, CancellationToken cancellationToken);
}
