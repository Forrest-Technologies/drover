namespace Drover.Domain.Models;

/// <summary>How the answer to a sign-in comes back to Drover.</summary>
public enum ModelSignInWait
{
    /// <summary>The person's device receives the browser's return and hands on the query string it received.</summary>
    Return,

    /// <summary>
    /// The person enters a short code on the provider's page; Drover asks the provider until they approve, refuse,
    /// or the code runs out. Nothing comes back to the device.
    /// </summary>
    Poll,
}
