namespace Drover.Domain.Models;

/// <summary>How the answer to a sign-in comes back to Drover.</summary>
public enum ModelSignInWait
{
    /// <summary>The person's device receives the browser's return and hands on the query string it received.</summary>
    Return,
}
