namespace Drover.Analyzers;

/// <summary>Ids of the rules this project enforces. See docs/architecture/10-tooling.md.</summary>
public static class DiagnosticIds
{
    #region Style Rules

    public const string NoAsyncSuffix = "DRV0001";

    public const string GlobalUsingsOnly = "DRV0002";

    public const string RegionsRequired = "DRV0003";

    public const string NoStringConcatenation = "DRV0004";

    public const string NoSilentCatch = "DRV0005";

    #endregion

    #region Architecture Rules

    public const string TenancyShape = "DRV0006";

    public const string VendorContainment = "DRV0007";

    public const string DataAccessContainment = "DRV0008";

    #endregion
}
