namespace Drover.Domain.Files;

/// <summary>
/// What a stored file is, decided by the server from its bytes, never from what the browser said: a picture Drover
/// can show and a model can read, text a model can read, a document whose text the server reads out for a model (PDF,
/// Word, Excel, or PowerPoint), or anything else.
/// </summary>
public enum FileKind
{
    Image,
    Text,
    Document,
    Other,
}
