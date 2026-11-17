namespace Drover.Contracts.Files;

/// <summary>
/// A stored file, as the server found it. <see cref="Kind"/> is <c>image</c>, <c>text</c>, or <c>other</c>, decided
/// from the bytes. A picture has <see cref="Width"/> and <see cref="Height"/>; text has its first lines in
/// <see cref="Preview"/>. <see cref="Caption"/> is the line a bot wrote under a file it shared into a conversation.
/// </summary>
public sealed record FileResponse(
    Guid Id,
    string Name,
    string ContentType,
    string Kind,
    long Size,
    int? Width = null,
    int? Height = null,
    string? Preview = null,
    string? Caption = null);

/// <summary>The values of <see cref="FileResponse.Kind"/>.</summary>
public static class FileKinds
{
    #region Kinds

    public const string Image = "image";

    public const string Text = "text";

    public const string Other = "other";

    #endregion
}
