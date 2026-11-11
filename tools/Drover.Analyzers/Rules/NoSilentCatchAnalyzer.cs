namespace Drover.Analyzers.Rules;

/// <summary>DRV0005: a catch block logs, rethrows, or returns a value built from the exception.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoSilentCatchAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const string LoggerMetadataName = "Microsoft.Extensions.Logging.ILogger";

    private const string LoggerExtensionsMetadataName = "Microsoft.Extensions.Logging.LoggerExtensions";

    private const string LoggerMessageAttributeName = "LoggerMessageAttribute";

    #endregion

    #region Properties

    public static DiagnosticDescriptor Rule { get; } = RuleDescriptors.Style(
        DiagnosticIds.NoSilentCatch,
        "Silent catch",
        "This catch swallows the exception: log it, rethrow it, or return a value built from it",
        "Never catch and swallow. Log and rethrow, or log and handle with intent.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterCatchAnalysis);
    }

    #endregion

    #region Analysis

    private static void RegisterCatchAnalysis(CompilationStartAnalysisContext context)
    {
        var logger = context.Compilation.GetTypeByMetadataName(LoggerMetadataName);
        var loggerExtensions = context.Compilation.GetTypeByMetadataName(LoggerExtensionsMetadataName);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeCatch(nodeContext, logger, loggerExtensions),
            SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatch(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? logger,
        INamedTypeSymbol? loggerExtensions)
    {
        var clause = (CatchClauseSyntax)context.Node;
        var nodes = NodesInHandler(clause);

        var model = context.SemanticModel;

        if (nodes.Any(IsRethrow) || nodes.Any(node => IsLogCall(node, model, logger, loggerExtensions)))
        {
            return;
        }

        if (ReturnsValueBuiltFromException(clause, nodes, model))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, clause.CatchKeyword.GetLocation()));
    }

    private static List<SyntaxNode> NodesInHandler(CatchClauseSyntax clause) =>
        [..clause.Block.DescendantNodes(
            node => node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)];

    private static bool IsRethrow(SyntaxNode node) =>
        node is ThrowStatementSyntax or ThrowExpressionSyntax;

    private static bool IsLogCall(
        SyntaxNode node,
        SemanticModel model,
        INamedTypeSymbol? logger,
        INamedTypeSymbol? loggerExtensions)
    {
        if (node is not InvocationExpressionSyntax invocation
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(method.ContainingType, loggerExtensions)
            || SymbolEqualityComparer.Default.Equals(method.ContainingType, logger)
            || method.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == LoggerMessageAttributeName);
    }

    private static bool ReturnsValueBuiltFromException(
        CatchClauseSyntax clause,
        List<SyntaxNode> nodes,
        SemanticModel model)
    {
        var declaration = clause.Declaration;

        if (declaration is null
            || declaration.Identifier.IsKind(SyntaxKind.None)
            || model.GetDeclaredSymbol(declaration) is not ILocalSymbol exception)
        {
            return false;
        }

        return nodes
            .OfType<ReturnStatementSyntax>()
            .Where(statement => statement.Expression is not null)
            .Any(statement => ReferencesLocal(statement.Expression!, exception, model));
    }

    private static bool ReferencesLocal(ExpressionSyntax expression, ILocalSymbol local, SemanticModel model) =>
        expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(name => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, local));

    #endregion
}
