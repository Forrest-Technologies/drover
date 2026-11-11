namespace Drover.Analyzers.Rules;

/// <summary>DRV0004: strings are built with interpolation, never with the + operator.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoStringConcatenationAnalyzer : DiagnosticAnalyzer
{
    #region Properties

    public static DiagnosticDescriptor Rule { get; } = RuleDescriptors.Style(
        DiagnosticIds.NoStringConcatenation,
        "String concatenation with +",
        "Build this string with interpolation instead of '+'",
        "Use $\"...\" for every string that is not a constant. Concatenation of constants is allowed.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeBinary, OperationKind.Binary);
        context.RegisterOperationAction(AnalyzeCompoundAssignment, OperationKind.CompoundAssignment);
    }

    #endregion

    #region Analysis

    private static void AnalyzeBinary(OperationAnalysisContext context)
    {
        var operation = (IBinaryOperation)context.Operation;

        if (IsStringConcatenation(operation) && !IsPartOfLargerConcatenation(operation))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeCompoundAssignment(OperationAnalysisContext context)
    {
        var operation = (ICompoundAssignmentOperation)context.Operation;

        if (operation.OperatorKind is BinaryOperatorKind.Add && operation.Type?.SpecialType is SpecialType.System_String)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation()));
        }
    }

    private static bool IsStringConcatenation(IBinaryOperation operation) =>
        operation.OperatorKind is BinaryOperatorKind.Add
        && operation.Type?.SpecialType is SpecialType.System_String
        && !operation.ConstantValue.HasValue;

    private static bool IsPartOfLargerConcatenation(IBinaryOperation operation)
    {
        var parent = operation.Parent;

        while (parent is IParenthesizedOperation)
        {
            parent = parent.Parent;
        }

        return parent is IBinaryOperation outer && IsStringConcatenation(outer);
    }

    #endregion
}
