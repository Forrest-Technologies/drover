namespace Drover.Engine.Files;

/// <summary>A stored file as a part of the message that carries it.</summary>
public static class StoredFileParts
{
    #region Public Methods

    /// <summary>
    /// A picture as an image part, anything else as a file part, with the row's facts copied in. A document a
    /// person attached carries its reading note as its caption, so the conversation shows it under the file.
    /// </summary>
    public static MessagePart ToPart(this StoredFile file, string? caption = null) =>
        FileInspector.KindOf(file.ContentType) is FileKind.Image
            ? new ImagePart(file.Id, file.ContentType, null, file.Name, file.Size, file.Width, file.Height, caption)
            : new FilePart(file.Id, file.Name, file.ContentType, file.Size, file.Preview, caption ?? file.ReadingNote);

    #endregion
}
