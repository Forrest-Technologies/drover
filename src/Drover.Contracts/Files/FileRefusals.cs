namespace Drover.Contracts.Files;

/// <summary>
/// Why a file cannot go with a message, as the <c>refusal</c> member of a refused upload's problem. The problem's
/// title says the same in a sentence that names the limit.
/// </summary>
public static class FileRefusals
{
    #region Refusals

    /// <summary>Larger than the server's <c>MaxFileBytes</c>.</summary>
    public const string TooLarge = "too_large";

    /// <summary>More files than the server's <c>MaxFilesPerMessage</c>.</summary>
    public const string TooMany = "too_many";

    /// <summary>The space would hold more than the server's <c>MaxSpaceFileMegabytes</c>.</summary>
    public const string SpaceFull = "space_full";

    /// <summary>The upload took longer than the server keeps an uncommitted byte reservation alive.</summary>
    public const string UploadExpired = "upload_expired";

    /// <summary>A picture, for a bot whose model cannot read pictures and that has no sandbox.</summary>
    public const string ModelCannotReadImages = "model_cannot_read_images";

    /// <summary>Neither a picture nor text, for a bot with no sandbox.</summary>
    public const string NothingCanOpenIt = "nothing_can_open_it";

    /// <summary>
    /// A file the bot could open only in its sandbox, in a private conversation of a space where others can reach the
    /// sandbox. Such a file is never copied there.
    /// </summary>
    public const string PrivateConversation = "private_conversation";

    /// <summary>The server could not write to its file store.</summary>
    public const string StorageUnavailable = "storage_unavailable";

    #endregion
}
