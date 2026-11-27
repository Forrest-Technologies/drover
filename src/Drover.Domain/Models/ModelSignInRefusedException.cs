namespace Drover.Domain.Models;

/// <summary>
/// A sign-in did not end with an approval: the person refused, the provider refused, or what came back does not
/// belong to the attempt. Nothing was kept. The message is Drover's own sentence for the person, never anything the
/// provider sent: "Sign-in was cancelled." unless the adapter names a plainer reason, such as a plan use the person did
/// not allow. <see cref="Kept"/> is what the provider registered for the person and asks a later sign-in to send again
/// (such as a client id it issued), when it signed them in but the sign-in is not used; it holds no token.
/// </summary>
public sealed class ModelSignInRefusedException(string message = ModelSignInRefusedException.Cancelled) : Exception(message)
{
    #region Private Fields

    public const string Cancelled = "Sign-in was cancelled.";

    #endregion

    #region Properties

    public IReadOnlyDictionary<string, string> Kept { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    #endregion
}
