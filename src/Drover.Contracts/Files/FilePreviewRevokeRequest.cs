namespace Drover.Contracts.Files;

/// <summary>Closes one outstanding HTML preview grant issued to the caller.</summary>
public sealed record FilePreviewRevokeRequest(string Grant);
