namespace Drover.Contracts.Models;

/// <summary>
/// The result of asking the provider for its models. When the provider could not answer, the catalog already
/// stored is returned unchanged and <see cref="Message"/> says why.
/// </summary>
public sealed record ModelRefreshResponse(bool Succeeded, string? Message, string? Failure, IReadOnlyList<ModelCatalogResponse> Models);
