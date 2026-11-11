namespace Drover.Analyzers.CodeFixes;

/// <summary>Removes a local using directive and adds it to the project's GlobalUsings.cs as a global using.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GlobalUsingsOnlyCodeFix))]
[Shared]
public sealed class GlobalUsingsOnlyCodeFix : CodeFixProvider
{
    #region Private Fields

    private const string GlobalUsingsFileName = "GlobalUsings.cs";

    private static readonly SymbolDisplayFormat qualifiedNameFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            (SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes)
            | SymbolDisplayMiscellaneousOptions.ExpandNullable
            | SymbolDisplayMiscellaneousOptions.ExpandValueTuple);

    #endregion

    #region Properties

    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.GlobalUsingsOnly];

    #endregion

    #region Public Methods

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];

        if (root?.FindNode(diagnostic.Location.SourceSpan) is not UsingDirectiveSyntax directive)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken);

        if (directive.Name is null
            || model?.GetSymbolInfo(directive.Name, context.CancellationToken).Symbol
                is not INamespaceOrTypeSymbol target)
        {
            return;
        }

        var qualifiedName = target.ToDisplayString(qualifiedNameFormat);
        var action = CodeAction.Create(
            "Move to GlobalUsings.cs",
            token => MoveToGlobalUsings(context.Document, directive, qualifiedName, token),
            nameof(GlobalUsingsOnlyCodeFix));

        context.RegisterCodeFix(action, diagnostic);
    }

    #endregion

    #region Private Methods

    private static async Task<Solution> MoveToGlobalUsings(
        Document document,
        UsingDirectiveSyntax directive,
        string qualifiedName,
        CancellationToken token)
    {
        var qualifiedDirective = directive.WithName(SyntaxFactory.ParseName(qualifiedName));
        var root = await document.GetSyntaxRootAsync(token);
        var withoutDirective = root!.RemoveNode(directive, SyntaxRemoveOptions.KeepNoTrivia)!;
        var solution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, withoutDirective);

        return await AddGlobalUsing(solution, document.Project.Id, qualifiedDirective, token);
    }

    private static async Task<Solution> AddGlobalUsing(
        Solution solution,
        ProjectId projectId,
        UsingDirectiveSyntax directive,
        CancellationToken token)
    {
        var project = solution.GetProject(projectId)!;
        var existing = project.Documents.FirstOrDefault(candidate => candidate.Name == GlobalUsingsFileName);
        var line = $"global {directive.WithoutTrivia()}";

        if (existing is null)
        {
            return solution.AddDocument(DocumentId.CreateNewId(projectId), GlobalUsingsFileName, $"{line}\n");
        }

        if (await AlreadyImported(existing, directive, token))
        {
            return solution;
        }

        var text = await existing.GetTextAsync(token);
        var separator = text.Length == 0 || text.ToString().EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n";
        return solution.WithDocumentText(existing.Id, text.Replace(text.Length, 0, $"{separator}{line}\n"));
    }

    private static async Task<bool> AlreadyImported(
        Document document,
        UsingDirectiveSyntax directive,
        CancellationToken token)
    {
        if (await document.GetSyntaxRootAsync(token) is not CompilationUnitSyntax root)
        {
            return false;
        }

        var model = await document.GetSemanticModelAsync(token);
        var qualifiedName = directive.Name!.ToString();

        return root.Usings.Any(existing =>
            existing.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
            && existing.Alias is null
            && existing.StaticKeyword.RawKind == directive.StaticKeyword.RawKind
            && existing.Name is not null
            && model?.GetSymbolInfo(existing.Name, token).Symbol
                ?.ToDisplayString(qualifiedNameFormat) == qualifiedName);
    }

    #endregion
}
