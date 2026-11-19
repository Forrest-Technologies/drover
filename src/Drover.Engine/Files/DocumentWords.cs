namespace Drover.Engine.Files;

/// <summary>
/// A document in words: what a model is told around the text read out of it, and the note a person sees under it
/// when a bot reads less than all of it. Both say plainly where it was cut.
/// </summary>
public static class DocumentWords
{
    #region Public Methods

    /// <summary>The line before a document's text, its text, and the line after it that says whether it was all.</summary>
    public static string ForModel(string name, string contentType, DocumentContent read, int maxCharacters) =>
        $"[The person attached {name}, {Describe(contentType, read)}. Its text follows.]\n{read.Text}\n{Ending(name, read, maxCharacters)}";

    /// <summary>What a model is told of a document with no text: that its pages follow as pictures, or that there is nothing to read.</summary>
    public static string WithoutText(string name, string contentType, DocumentContent read) => read.PageImages.Count switch
    {
        0 => $"[The person attached {name}, {Describe(contentType, read)}, but it has no text, and no picture of its pages could be given to you.]",
        var shown when shown < read.Total =>
            $"[The person attached {name}, {Describe(contentType, read)}. It has no text, so pictures of its pages follow, from the first. Only {shown} of its {read.Total} pages are shown.]",
        _ => $"[The person attached {name}, {Describe(contentType, read)}. It has no text, so pictures of its pages follow, from the first.]",
    };

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
        _ => "It has no text a bot can read.",
    };

    /// <summary>The note under a document the server could not read.</summary>
    public static string Unreadable(string reason) => $"A bot cannot read it: {reason}.";

    #endregion

    #region Private Methods

    private static string Ending(string name, DocumentContent read, int maxCharacters) => read switch
    {
        { TextCut: true } => $"[{name} continues. Only its first {maxCharacters} characters are shown.]",
        { PagesCut: true } => $"[{name} continues. Only its first {read.Read} of its {read.Total} {read.Unit} are shown.]",
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
