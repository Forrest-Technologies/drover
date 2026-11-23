namespace Drover.Contracts.Models;

/// <summary>The words the server sends for a model or tool connection's status.</summary>
public static class ConnectionStatuses
{
    #region Statuses

    public const string Pending = "pending";

    public const string Ready = "ready";

    public const string Failed = "failed";

    public const string Disabled = "disabled";

    #endregion
}
