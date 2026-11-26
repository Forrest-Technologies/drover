namespace Drover.Contracts.Models;

/// <summary>
/// A named provider the server offers: where its chat protocol answers and whether it needs a key. It carries no
/// secret. <see cref="KeyRule"/> is one of <see cref="ModelProviderKeyRules"/>.
/// </summary>
public sealed record ModelProviderPresetResponse(
    string Id,
    string DisplayName,
    string Address,
    string KeyRule,
    string? KeyPage,
    bool Local);

/// <summary>The words the server sends for whether a named provider needs a key.</summary>
public static class ModelProviderKeyRules
{
    #region Rules

    public const string Required = "required";

    public const string Optional = "optional";

    public const string None = "none";

    #endregion
}
