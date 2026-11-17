namespace Drover.Ui.Chat;

/// <summary>
/// The person reading a conversation, as far as what a reply used goes. It mirrors the server's rule for a run's
/// usage: a space owner or admin reads any run in a conversation they can open, anyone else only the runs that act for
/// them. A reply whose run the server would refuse offers nothing to open.
/// </summary>
public sealed record UsageReader(Guid UserId, bool IsSpaceAdmin)
{
    #region Public Methods

    public bool MayRead(Guid? runPrincipalUserId) => IsSpaceAdmin || runPrincipalUserId == UserId;

    #endregion
}
