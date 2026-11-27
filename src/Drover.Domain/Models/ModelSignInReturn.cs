namespace Drover.Domain.Models;

/// <summary>
/// What came back from the provider's page: the query string the person's device received. It holds a one-time
/// code, so it prints nothing of itself.
/// </summary>
public sealed record ModelSignInReturn(string Text)
{
    #region Public Methods

    public override string ToString() => $"{nameof(ModelSignInReturn)} [secret]";

    #endregion
}
