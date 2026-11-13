namespace Drover.Client;

/// <summary>The status page of a space.</summary>
public sealed partial class DroverApi
{
    #region Status

    /// <summary>
    /// The space's connections as last checked and, where the deployment allows, the server's own rows (owner or
    /// admin only). Nothing is called to build it: every row says what was last seen.
    /// </summary>
    public Task<StatusReport> GetStatus(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<StatusReport>($"{Space(spaceId)}/status", cancellationToken);

    #endregion
}
