namespace Drover.Analyzers.Rules;

/// <summary>DRV0001: a method name does not end in Async unless something outside Drover or a sync twin requires it.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoAsyncSuffixAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const string Suffix = "Async";

    #endregion

    #region Properties

    public static DiagnosticDescriptor Rule { get; } = RuleDescriptors.Style(
        DiagnosticIds.NoAsyncSuffix,
        "Method name ends in Async",
        "Rename '{0}' to '{1}': Drover method names carry no Async suffix",
        "The Task return type is the signal. Keep the suffix only on an override, on an implementation of a "
        + "member declared outside Drover, or where a synchronous method of the same name exists in the type.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    #endregion

    #region Analysis

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (!NeedsRename(method))
        {
            return;
        }

        var newName = method.Name.Substring(0, method.Name.Length - Suffix.Length);
        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0], method.Name, newName));
    }

    private static bool NeedsRename(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary
        && method.Name.Length > Suffix.Length
        && method.Name.EndsWith(Suffix, StringComparison.Ordinal)
        && !method.IsOverride
        && method.ExplicitInterfaceImplementations.IsEmpty
        && !ImplementsExternalInterfaceMember(method)
        && !HasSynchronousTwin(method);

    private static bool ImplementsExternalInterfaceMember(IMethodSymbol method) =>
        method.ContainingType.AllInterfaces
            .SelectMany(contract => contract.GetMembers(method.Name).OfType<IMethodSymbol>())
            .Where(member => !IsDeclaredInDrover(member))
            .Any(member => SymbolEqualityComparer.Default.Equals(
                method.ContainingType.FindImplementationForInterfaceMember(member),
                method));

    private static bool HasSynchronousTwin(IMethodSymbol method)
    {
        var twinName = method.Name.Substring(0, method.Name.Length - Suffix.Length);
        return method.ContainingType.GetMembers(twinName).OfType<IMethodSymbol>().Any();
    }

    private static bool IsDeclaredInDrover(ISymbol symbol)
    {
        var assembly = symbol.ContainingAssembly.Name;
        return assembly == "Drover" || assembly.StartsWith("Drover.", StringComparison.Ordinal);
    }

    #endregion
}
