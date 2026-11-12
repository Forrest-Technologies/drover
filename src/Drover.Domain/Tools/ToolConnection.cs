namespace Drover.Domain.Tools;

/// <summary>A remote tool server. Its tools are rows in <c>tools</c>.</summary>
public sealed class ToolConnection : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public required string Url { get; set; }

    public ToolTransport Transport { get; set; } = ToolTransport.StreamableHttp;

    public ToolAuthKind AuthKind { get; set; } = ToolAuthKind.None;

    public CredentialScope CredentialScope { get; set; } = CredentialScope.Space;

    public OAuthMetadata? Oauth { get; set; }

    public TrustLevel Trust { get; set; } = TrustLevel.Untrusted;

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Pending;

    public ProtocolMode ProtocolMode { get; set; } = ProtocolMode.Automatic;

    /// <summary>The protocol version that was negotiated, for the badge on the connection.</summary>
    public string? NegotiatedProtocol { get; set; }

    public DateTimeOffset? LastListedAt { get; set; }

    public string? LastError { get; set; }

    #endregion
}
