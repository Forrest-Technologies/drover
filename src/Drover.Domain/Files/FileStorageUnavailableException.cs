namespace Drover.Domain.Files;

/// <summary>The store could not write the destination for an upload.</summary>
public sealed class FileStorageUnavailableException(Type failureType) : IOException("The file store could not save the upload.")
{
    public Type FailureType { get; } = failureType;
}
