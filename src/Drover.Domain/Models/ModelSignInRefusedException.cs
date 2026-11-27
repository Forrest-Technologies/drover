namespace Drover.Domain.Models;

/// <summary>
/// A sign-in did not end with an approval: the person refused, the provider refused, or what came back does not
/// belong to the attempt. Nothing was kept. The message is Drover's own sentence for the person, never anything the
/// provider sent: "Sign-in was cancelled." unless the adapter names a plainer reason, such as a plan use the person did
/// not allow.
/// </summary>
public sealed class ModelSignInRefusedException(string message = ModelSignInRefusedException.Cancelled) : Exception(message)
{
    #region Constants

    public const string Cancelled = "Sign-in was cancelled.";

    #endregion
}
