namespace Drover.Analyzers;

/// <summary>
/// DRV0006. An entity exposed as a <c>DbSet</c> must say whose rows it holds: it implements
/// <c>ISpaceOwned</c> or <c>IUserOwned</c>, or it is listed in <c>GlobalTables.manifest</c>. Without that the
/// isolation suite cannot tell which row-level security policy the table needs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenancyShapeAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const string ManifestFileName = "GlobalTables.manifest";

    private const string DbContextName = "Microsoft.EntityFrameworkCore.DbContext";

    private const string DbSetName = "Microsoft.EntityFrameworkCore.DbSet`1";

    private static readonly string[] ownershipInterfaces =
    [
        "Drover.Domain.Tenancy.ISpaceOwned",
        "Drover.Domain.Tenancy.IUserOwned",
    ];

    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.TenancyShape,
        "An entity exposed as a DbSet must be space-owned, user-owned, or listed as global",
        "Entity '{0}' is exposed as DbSet '{1}' but implements neither ISpaceOwned nor IUserOwned and is not listed "
            + "in GlobalTables.manifest. Implement one of the interfaces, or list its table in GlobalTables.manifest "
            + "with the reason.",
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
        var dbContext = start.Compilation.GetTypeByMetadataName(DbContextName);
        var dbSet = start.Compilation.GetTypeByMetadataName(DbSetName);

        if (dbContext is null || dbSet is null)
        {
            return;
        }

        var globalEntities = ReadGlobalEntities(start.Options.AdditionalFiles, start.CancellationToken);
        start.RegisterSymbolAction(
            context => AnalyzeProperty(context, dbContext, dbSet, globalEntities),
            SymbolKind.Property);
    }

    private static void AnalyzeProperty(
        SymbolAnalysisContext context,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        HashSet<string> globalEntities)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (!IsDbSetOfNamedEntity(property, dbSet, out var entity) || !DerivesFrom(property.ContainingType, dbContext))
        {
            return;
        }

        if (IsOwned(entity) || globalEntities.Contains(entity.Name))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(rule, property.Locations[0], entity.Name, property.Name));
    }

    #endregion

    #region Symbol Checks

    private static bool IsDbSetOfNamedEntity(IPropertySymbol property, INamedTypeSymbol dbSet, out INamedTypeSymbol entity)
    {
        entity = null!;

        if (property.Type is not INamedTypeSymbol { IsGenericType: true } type
            || !SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, dbSet)
            || type.TypeArguments[0] is not INamedTypeSymbol named)
        {
            return false;
        }

        entity = named;
        return true;
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOwned(INamedTypeSymbol entity) =>
        entity.AllInterfaces.Any(contract => ownershipInterfaces.Contains(contract.ToDisplayString()));

    #endregion

    #region Manifest

    private static HashSet<string> ReadGlobalEntities(ImmutableArray<AdditionalText> files, CancellationToken cancellationToken)
    {
        var entities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files.Where(file => Path.GetFileName(file.Path) == ManifestFileName))
        {
            if (file.GetText(cancellationToken) is not { } text)
            {
                continue;
            }

            foreach (var line in text.Lines)
            {
                AddEntity(entities, line.ToString());
            }
        }

        return entities;
    }

    private static void AddEntity(HashSet<string> entities, string line)
    {
        var comment = line.IndexOf('#');
        var content = comment < 0 ? line : line.Substring(0, comment);
        var words = content.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (words.Length >= 2)
        {
            entities.Add(words[1]);
        }
    }

    #endregion
}
