namespace Drover.Domain.Models;

/// <summary>How a provider says it handles the prompts sent to one model, in the provider's own terms.</summary>
public enum PrivacyMode
{
    Unknown,
    Private,
    Anonymized,
    Attested,
}
