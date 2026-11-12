namespace Drover.Analyzers.Rules;

/// <summary>DRV0002: using directives are global and live in GlobalUsings.cs. Aliases are the one exception.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GlobalUsingsOnlyAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const string GlobalUsingsFileName = "GlobalUsings.cs";

    #endregion

    #region Properties

    public static DiagnosticDescriptor Rule { get; } = RuleDescriptors.Style(
        DiagnosticIds.GlobalUsingsOnly,
        "Local using directive",
        "Move 'using {0}' to GlobalUsings.cs as a global using",
        "Every using directive is global and lives in one GlobalUsings.cs per project. "
        + "An alias (using X = Y;) that resolves a genuine ambiguity is the one exception.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeDirective, SyntaxKind.UsingDirective);
    }

    #endregion

    #region Analysis

    private static void AnalyzeDirective(SyntaxNodeAnalysisContext context)
    {
        var directive = (UsingDirectiveSyntax)context.Node;

        if (IsAllowed(directive))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, directive.GetLocation(), directive.Name?.ToString()));
    }

    private static bool IsAllowed(UsingDirectiveSyntax directive) =>
        directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
        || directive.Alias is not null
        || directive.Name is null
        || Path.GetFileName(directive.SyntaxTree.FilePath) == GlobalUsingsFileName;

    #endregion
}
