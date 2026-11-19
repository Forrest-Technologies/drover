namespace Drover.Contracts.Files;

/// <summary>Where one authorized HTML preview may be opened, or why preview is unavailable.</summary>
public sealed record FilePreviewResponse(string? Address, string? Problem = null, string? Grant = null);
