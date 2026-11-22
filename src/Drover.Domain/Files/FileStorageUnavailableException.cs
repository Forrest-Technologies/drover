namespace Drover.Domain.Files;

/// <summary>The store could not write the destination for an upload.</summary>
public sealed class FileStorageUnavailableException(Exception cause)
    : IOException("The file store could not save the upload.", cause);
