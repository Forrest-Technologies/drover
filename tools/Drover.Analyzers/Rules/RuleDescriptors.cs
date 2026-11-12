namespace Drover.Analyzers.Rules;

/// <summary>Builds the descriptors every rule shares the shape of.</summary>
internal static class RuleDescriptors
{
    #region Public Methods

    public static DiagnosticDescriptor Style(string id, string title, string messageFormat, string description) =>
        Build(id, title, messageFormat, description, "Drover.Style");

    public static DiagnosticDescriptor Architecture(string id, string title, string messageFormat, string description) =>
        Build(id, title, messageFormat, description, "Drover.Architecture");

    #endregion

    #region Private Methods

    private static DiagnosticDescriptor Build(
        string id,
        string title,
        string messageFormat,
        string description,
        string category) =>
        new(
            id,
            title,
            messageFormat,
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: description);

    #endregion
}
