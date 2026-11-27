namespace Drover.Domain.Decisions;

/// <summary>
/// The one decision model of a space, which checks the bot rules written as a sentence. A kind (a string, because
/// each adapter names its own), where its provider is (an address or an account, as the kind's rule says), the model,
/// and the key in the vault. Only a connection that passed its last test is handed to a run.
/// </summary>
public sealed class DecisionConnection : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public required string Kind { get; set; }

    /// <summary>The address a person gave, for a kind whose rule is <see cref="DecisionAddressRule.Address"/>.</summary>
    public string? Address { get; set; }

    /// <summary>The account id a person gave, for a kind whose rule is <see cref="DecisionAddressRule.Account"/>.</summary>
    public string? Account { get; set; }

    public required string Model { get; set; }

    /// <summary>The key in the vault. A plain column, not a foreign key, as for a model connection.</summary>
    public Guid? KeySecretId { get; set; }

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Pending;

    public DateTimeOffset? TestedAt { get; set; }

    /// <summary>What the last failed test said, in the words the failure maps to. Never the key or the provider's own text.</summary>
    public string? LastError { get; set; }

    /// <summary>What a million input tokens cost, when the person knows it. Null leaves a check unpriced.</summary>
    public decimal? InputPricePerMillion { get; set; }

    public Guid? CreatedByUserId { get; set; }

    #endregion
}
