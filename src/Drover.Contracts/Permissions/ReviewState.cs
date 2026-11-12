namespace Drover.Contracts.Permissions;

/// <summary>Whether a human has confirmed the tool definition a grant was given for. Unconfirmed counts as ask.</summary>
public enum ReviewState
{
    Current,
    NeedsReview,
}
