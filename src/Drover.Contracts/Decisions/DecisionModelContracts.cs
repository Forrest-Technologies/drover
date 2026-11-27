namespace Drover.Contracts.Decisions;

/// <summary>
/// A kind of decision model this server offers. The key rule is "required", "optional" or "none"; the address rule
/// is "fixed" (nothing to give), "address" (an address to give) or "account" (an account id to give).
/// </summary>
public sealed record DecisionModelKindResponse(string Kind, string DisplayName, string KeyRule, string AddressRule);

/// <summary>
/// The space's decision model. <see cref="HasKey"/> says a key is stored; the key is never returned. The status is
/// "pending", "ready" or "failed"; only "ready" is handed to a run.
/// </summary>
public sealed record DecisionModelResponse(
    string Kind,
    string DisplayName,
    string? Address,
    string? Account,
    string Model,
    bool HasKey,
    string Status,
    DateTimeOffset? TestedAt,
    string? LastError,
    decimal? InputPricePerMillion);

/// <summary>
/// What the decision model screen needs: the connection, or null when there is none; how many enabled rules written
/// as a sentence stop being checked if it goes; the kinds this server offers; and whether this server judges rules
/// at all (deployment rule AllowJudgedRules).
/// </summary>
public sealed record DecisionModelState(
    DecisionModelResponse? Connection,
    int JudgedRules,
    IReadOnlyList<DecisionModelKindResponse> Kinds,
    bool JudgedRulesAllowed);

/// <summary>
/// Input only: connects the space's decision model, or replaces the one it has. The key may be left out to keep the
/// stored one when only the model or the price changes on the same kind and address.
/// </summary>
public sealed record SetDecisionModelRequest(
    string? Kind,
    string? Address,
    string? Account,
    string? Model,
    string? ApiKey,
    decimal? InputPricePerMillion = null)
{
    public override string ToString() => nameof(SetDecisionModelRequest);
}

/// <summary>Input only: the new key for the space's decision model. Never part of a response.</summary>
public sealed record ReplaceDecisionKeyRequest(string? ApiKey)
{
    public override string ToString() => nameof(ReplaceDecisionKeyRequest);
}

/// <summary>What a test found: whether the model answered, how long it took or what is wrong, and the connection after it.</summary>
public sealed record DecisionModelTestResponse(bool Succeeded, string? Failure, string Message, DecisionModelResponse Connection);
