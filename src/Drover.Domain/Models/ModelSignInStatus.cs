namespace Drover.Domain.Models;

public enum ModelSignInStatus
{
    Active,

    /// <summary>The provider ended the sign-in. The row stays, so the person is told, until they sign in again or out.</summary>
    NeedsSignIn,
}
