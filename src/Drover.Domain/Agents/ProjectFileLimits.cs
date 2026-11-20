namespace Drover.Domain.Agents;

/// <summary>
/// Per-model-call bounds for shared project files: file count, stored bytes, rendered text characters, image count,
/// encoded image bytes, decoded image bytes, and image dimensions.
/// </summary>
public static class ProjectFileLimits
{
    #region Public Fields

    /// <summary>The most project files opened for one model call before any content is materialized.</summary>
    public const int MaxFiles = 12;

    /// <summary>The most stored bytes across project files opened for one model call before reading content.</summary>
    public const long MaxStoredBytes = 10L * 1024 * 1024;

    /// <summary>The most characters of text inside one model call's project-files message.</summary>
    public const int MaxCharacters = 120_000;

    /// <summary>The most images (standalone pictures and scanned PDF pages) inside one model call's project-files message.</summary>
    public const int MaxImages = 20;

    /// <summary>The most encoded image bytes across project files in one model call.</summary>
    public const long MaxImageBytes = 10L * 1024 * 1024;

    /// <summary>The most decoded RGBA pixel bytes (width * height * 4) across project files in one model call.</summary>
    public const long MaxDecodedImageBytes = 64L * 1024 * 1024;

    /// <summary>The largest width or height in pixels allowed for a project image or PDF page image.</summary>
    public const int MaxImageDimension = 4_096;

    /// <summary>The most pixels allowed in one project image or PDF page image.</summary>
    public const long MaxImagePixels = 16L * 1024 * 1024;

    /// <summary>The most lightweight metadata candidates loaded per model call when selecting project files.</summary>
    public const int CandidateLimit = MaxFiles * 4;

    #endregion
}
