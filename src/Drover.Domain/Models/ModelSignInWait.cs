namespace Drover.Domain.Models;

/// <summary>How the answer to a sign-in comes back to Drover.</summary>
public enum ModelSignInWait
{
    /// <summary>The person's device receives the browser's return and hands on the query string it received.</summary>
    Return,

    /// <summary>
    /// The provider's own page shows what to copy, and the person pastes it into Drover, which hands it on. For a device
    /// that cannot listen for the browser's return.
    /// </summary>
    Paste,
}
