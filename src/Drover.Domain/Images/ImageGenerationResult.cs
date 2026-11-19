namespace Drover.Domain.Images;

/// <summary>An image and the provider usage reported for making it.</summary>
public sealed record ModelImageResult(ReadOnlyMemory<byte> Data, string MediaType, string Model)
{
    #region Properties

    public ModelImageUsage Usage { get; init; } = new();

    #endregion
}


/// <summary>Provider usage for one generated image. Null cost means the provider gave no reliable price.</summary>
public sealed record ModelImageUsage
{
    #region Properties

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public decimal? Cost { get; init; }

    public string Currency { get; init; } = "USD";

    #endregion
}
