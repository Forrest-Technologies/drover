namespace Drover.Server.Push;

/// <summary>
/// Whether this server can wake devices, with the key pair it signs with, or the reason it cannot in a sentence for
/// the operator. Push signs every request with a contact address for the push service, and that address is the
/// server's own public https address.
/// </summary>
public sealed record PushAvailability(WebPushKeys? Keys, string? Problem)
{
    #region Properties

    public string? PublicKey => Keys?.PublicKey;

    public static string NoAddress =>
        $"Notifications are off: they need {PublicAddress.Variable} set to the https address people open Drover at.";

    #endregion

    #region Public Methods

    public static PushAvailability On(WebPushKeys keys) => new(keys, null);

    public static PushAvailability Off(string problem) => new(null, problem);

    /// <summary>The contact a push service sees, or null when the server has no public https address.</summary>
    public static string? SubjectFor(Uri? publicUrl) =>
        publicUrl is { Scheme: "https" } ? publicUrl.GetLeftPart(UriPartial.Authority) : null;

    #endregion
}
