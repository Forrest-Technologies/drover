namespace Drover.Domain.Images;

/// <summary>Generates one image through a model connection without exposing provider types.</summary>
public interface IModelImageGenerator
{
    string Kind { get; }

    Uri? FixedAddress => null;

    Task<ModelImageResult> Generate(
        ModelEndpoint connection,
        string? apiKey,
        ModelImageRequest request,
        CancellationToken cancellationToken);
}


/// <summary>Builds a compatible image request through the shared protocol implementation.</summary>
public delegate Task<ModelImageResult> CompatibleImageGenerator(
    ModelEndpoint connection,
    string? apiKey,
    ModelImageRequest request,
    CancellationToken cancellationToken);
