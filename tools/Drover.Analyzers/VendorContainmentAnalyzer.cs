namespace Drover.Analyzers;

/// <summary>
/// DRV0007. A vendor's namespace may be referenced only by the project that wraps it, plus the server's wiring, so a
/// vendor's breaking change reaches one project and never the rest of the code. Test projects are exempt.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VendorContainmentAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const string ServerAssembly = "Drover.Server";

    private const string TestSuffix = ".Tests";

    /// <summary>The configuration: each vendor namespace and the projects allowed to reference it.</summary>
    private static readonly (string Namespace, string[] Allowed)[] vendors =
    [
        ("Microsoft.Agents.AI", ["Drover.Agents", ServerAssembly]),
        ("Microsoft.Extensions.AI", ["Drover.Agents", "Drover.Adapters.OpenAICompatible", "Drover.Adapters.Venice", ServerAssembly]),
        ("ModelContextProtocol", ["Drover.Adapters.Mcp", ServerAssembly]),
    ];

    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.VendorContainment,
        "A vendor namespace may be referenced only by the project that wraps it",
        "'{0}' belongs to vendor namespace '{1}', which only {2} may reference. Use Drover's own interface for it, "
            + "and keep the vendor type inside its project (AGENTS.md section 4).",
        "Drover.Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    #endregion

    #region Properties

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(StartCompilation);
    }

    #endregion

    #region Compilation

    private static void StartCompilation(CompilationStartAnalysisContext start)
    {
        var assembly = start.Compilation.AssemblyName ?? string.Empty;

        if (assembly.EndsWith(TestSuffix, StringComparison.Ordinal))
        {
            return;
        }

        var forbidden = vendors.Where(vendor => !vendor.Allowed.Contains(assembly)).ToArray();

        if (forbidden.Length > 0)
        {
            start.RegisterSyntaxNodeAction(
                context => AnalyzeName(context, forbidden),
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName);
        }
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context, (string Namespace, string[] Allowed)[] forbidden)
    {
        if (IsDeclaredNamespace(context.Node) || IsLeftPartOfName(context.Node))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        var name = NamespaceOf(symbol);

        if (name is null)
        {
            return;
        }

        foreach (var (vendorNamespace, allowed) in forbidden)
        {
            if (IsWithin(name, vendorNamespace))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    rule,
                    context.Node.GetLocation(),
                    symbol!.ToDisplayString(),
                    vendorNamespace,
                    string.Join(", ", allowed)));
                return;
            }
        }
    }

    /// <summary>A namespace declaration names a namespace; it does not reference a vendor's.</summary>
    private static bool IsDeclaredNamespace(SyntaxNode node) =>
        node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(declaration => declaration.Name.Span.Contains(node.Span));

    /// <summary>
    /// Only the last part of a dotted name is reported, so <c>A.B.C</c> gives one diagnostic, not three.
    /// </summary>
    private static bool IsLeftPartOfName(SyntaxNode node)
    {
        for (var current = node; current.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax; current = current.Parent)
        {
            if (current.Parent is QualifiedNameSyntax qualified && qualified.Left == current)
            {
                return true;
            }

            if (current.Parent is MemberAccessExpressionSyntax access && access.Expression == current)
            {
                return true;
            }
        }

        return false;
    }

    private static string? NamespaceOf(ISymbol? symbol) => symbol switch
    {
        null => null,
        INamespaceSymbol { IsGlobalNamespace: false } namespaceSymbol => namespaceSymbol.ToDisplayString(),
        INamespaceSymbol => null,
        _ => symbol.ContainingNamespace is { IsGlobalNamespace: false } containing ? containing.ToDisplayString() : null,
    };

    private static bool IsWithin(string name, string vendorNamespace) =>
        name == vendorNamespace || name.StartsWith($"{vendorNamespace}.", StringComparison.Ordinal);

    #endregion
}
