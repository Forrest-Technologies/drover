namespace Drover.Engine.Files;

/// <summary>
/// A document in words: what a model is told around the text read out of it, and the note a person sees under it
/// when a bot reads less than all of it. Both say plainly where it was cut.
/// </summary>
public static class DocumentWords
{
    #region Private Fields

    /// <summary>The stored reading note for a document without readable text.</summary>
    public const string NoReadableText = "It has no text a bot can read.";

    private const string AttachmentCaption = "The person attached";

    private const string ProjectCaption = "The project shares";

    #endregion

    #region Public Methods

    /// <summary>
    /// The line before a document's text, its text, and the line after it that says whether it was all.
    /// </summary>
    public static string ForModel(string name, string contentType, DocumentContent read, int maxCharacters) =>
        WithText(name, contentType, read, maxCharacters, AttachmentCaption);

    /// <summary>The project-shared form of <see cref="ForModel"/>.</summary>
    public static string ForProject(string name, string contentType, DocumentContent read, int maxCharacters) =>
        WithText(name, contentType, read, maxCharacters, ProjectCaption);

    /// <summary>What a model is told of a document without text, including any pictures of its pages.</summary>
    public static string WithoutText(string name, string contentType, DocumentContent read) =>
        WithoutText(name, contentType, read, AttachmentCaption);

    /// <summary>The project-shared form of <see cref="WithoutText(string, string, DocumentContent)"/>.</summary>
    public static string WithoutProjectText(string name, string contentType, DocumentContent read) =>
        WithoutText(name, contentType, read, ProjectCaption);

    /// <summary>
    /// The note under a document a person attached, or null when a bot reads all of it. Said once, when it is
    /// uploaded, from what the bot can do then.
    /// </summary>
    public static string? ForPerson(DocumentContent read, int maxCharacters, bool modelReadsImages) => read switch
    {
        { TextCut: true } => $"A bot reads only its first {Number(maxCharacters)} characters.",
        { HasText: true, PagesCut: true } => $"A bot reads only its first {read.Read} of its {read.Total} {read.Unit}.",
        { HasText: true } => null,
        _ when modelReadsImages && read.Unit is "pages" => "It has no text, so a bot sees its first pages as pictures.",
        _ => NoReadableText,
    };

    /// <summary>The note under a document the server could not read.</summary>
    public static string Unreadable(string reason) => $"A bot cannot read it: {reason}.";

    #endregion

    #region Private Methods

    private static string WithText(
        string name,
        string contentType,
        DocumentContent read,
        int maxCharacters,
        string source) =>
        $"""
        [{source} {name}, {Describe(contentType, read)}. Its text follows.]
        {read.Text}
        {Ending(name, read, maxCharacters)}
        """;

    private static string WithoutText(string name, string contentType, DocumentContent read, string source)
    {
        var description = $"{source} {name}, {Describe(contentType, read)}";
        var firstPages = "It has no text, so pictures of its pages follow, from the first.";
        return read.PageImages.Count switch
        {
            0 => $"[{description}, but it has no text, and no picture of its pages could be given to you.]",
            var shown when shown < read.Total => $"[{description}. {firstPages} {ShownPages(shown, read.Total)}]",
            _ => $"[{description}. {firstPages}]",
        };
    }

    private static string ShownPages(int shown, int total) =>
        $"Only {shown} of its {total} pages {(shown is 1 ? "is" : "are")} shown.";


    private static string Ending(string name, DocumentContent read, int maxCharacters) => read switch
    {
        { TextCut: true } => $"[{name} continues. Only its first {maxCharacters} characters are shown.]",
        { PagesCut: true } =>
            $"[{name} continues. Only its first {read.Read} of its {read.Total} {read.Unit} are shown.]",
        _ => $"[End of {name}.]",
    };

    /// <summary>"a PDF document of 12 pages", "a Word document", "an Excel workbook of 3 sheets".</summary>
    private static string Describe(string contentType, DocumentContent read)
    {
        var kind = contentType switch
        {
            DocumentTypes.Pdf => "a PDF document",
            DocumentTypes.Word => "a Word document",
            DocumentTypes.Excel => "an Excel workbook",
            _ => "a PowerPoint presentation",
        };

        return read.Unit is { } unit ? $"{kind} of {read.Total} {(read.Total == 1 ? unit.TrimEnd('s') : unit)}" : kind;
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    #endregion
}
