namespace Drover.Domain.Files;

/// <summary>The content types of the documents whose text the server reads out for a model.</summary>
public static class DocumentTypes
{
    #region Properties

    // The inspector writes every content type in lowercase, so these compare exactly.
    public static IReadOnlySet<string> All { get; } = new HashSet<string>([Pdf, Word, Excel, PowerPoint]);

    #endregion

    #region Types

    public const string Pdf = "application/pdf";

    public const string Word = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public const string Excel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string PowerPoint = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    #endregion
}
