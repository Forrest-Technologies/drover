namespace Drover.Engine.Runs;

/// <summary>
/// What the rules said about one tool call when the model wrote it: the arguments as written, the arguments as the
/// rules left them, and the outcome, or null when no rule applied.
/// </summary>
public sealed record RuledCall(JsonElement Written, JsonElement Left, RuleOutcome? Outcome);
