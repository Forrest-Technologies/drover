namespace Drover.Analyzers.Rules;

/// <summary>
/// DRV0008: DroverDbContext, DbSet and IQueryable are used only in Drover.Data, and no public member there
/// returns IQueryable. Test projects are exempt.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DataAccessContainmentAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const string DataAssemblyName = "Drover.Data";

    private const string TestAssemblySuffix = ".Tests";

    private const string ContextName = "DroverDbContext";

    private const string DbSetName = "DbSet";

    private const string QueryableName = "IQueryable";

    private const string VarKeyword = "var";

    private static readonly string[] watchedNames = [ContextName, DbSetName, QueryableName, VarKeyword];

    #endregion

    #region Properties

    public static DiagnosticDescriptor Rule { get; } = RuleDescriptors.Architecture(
        DiagnosticIds.DataAccessContainment,
        "Data access outside Drover.Data",
        "{0}",
        "Every query lives in Drover.Data. Services call repositories, and repositories return entities or records, "
        + "never IQueryable. Test projects are exempt.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(RegisterByAssembly);
    }

    #endregion

    #region Analysis

    private static void RegisterByAssembly(CompilationStartAnalysisContext context)
    {
        var assemblyName = context.Compilation.AssemblyName ?? string.Empty;

        if (assemblyName.EndsWith(TestAssemblySuffix, StringComparison.Ordinal))
        {
            return;
        }

        if (assemblyName == DataAssemblyName)
        {
            context.RegisterSymbolAction(AnalyzePublicMember, SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);
            return;
        }

        context.RegisterSyntaxNodeAction(AnalyzeTypeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;

        if (!watchedNames.Contains(name.Identifier.ValueText))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(name).Symbol is INamedTypeSymbol type && IsRestricted(type))
        {
            var message = $"'{type.Name}' is for Drover.Data only: call a repository instead";
            context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), message));
        }
    }

    private static void AnalyzePublicMember(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        if (!IsExposedPublicly(symbol) || !ExposesQueryable(TypeOf(symbol)))
        {
            return;
        }

        var message = $"Public member '{symbol.Name}' returns IQueryable: return entities or records instead";
        context.ReportDiagnostic(Diagnostic.Create(Rule, symbol.Locations[0], message));
    }

    #endregion

    #region Type Tests

    private static bool IsRestricted(INamedTypeSymbol type) =>
        type.Name == ContextName
        || IsNamed(type.OriginalDefinition, DbSetName, "Microsoft.EntityFrameworkCore")
        || IsNamed(type.OriginalDefinition, QueryableName, "System.Linq");

    private static bool IsNamed(INamedTypeSymbol type, string name, string containingNamespace) =>
        type.Name == name && type.IsGenericType && type.ContainingNamespace?.ToDisplayString() == containingNamespace;

    private static bool IsExposedPublicly(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static ITypeSymbol? TypeOf(ISymbol symbol) =>
        symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Ordinary } method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };

    private static bool ExposesQueryable(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && (IsNamed(named.OriginalDefinition, QueryableName, "System.Linq") || named.TypeArguments.Any(ExposesQueryable));

    #endregion
}
