namespace Drover.Domain.Images;

/// <summary>One image requested from a model connection.</summary>
public sealed record ModelImageRequest(string Prompt, string Model, string Size, int MaxBytes);
