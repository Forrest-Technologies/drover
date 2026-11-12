namespace Drover.Domain.Models;

/// <summary>Why a provider call failed, in the terms the rest of Drover acts on.</summary>
public enum ModelFailure
{
    /// <summary>The key is missing, wrong, or revoked. The message tells the owner how to fix the connection.</summary>
    InvalidKey,

    /// <summary>The provider is limiting requests. Try again after <see cref="ModelProviderException.RetryAfter"/>.</summary>
    RetryLater,

    /// <summary>The account is out of credit or at a spend limit.</summary>
    OutOfCredit,

    /// <summary>The provider could not be reached or failed on its side.</summary>
    Unreachable,

    /// <summary>The provider refused the request for another reason.</summary>
    Rejected,
}
