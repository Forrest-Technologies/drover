namespace Drover.Analyzers.CodeFixes;

/// <summary>Removes a local using directive and adds it to the project's GlobalUsings.cs as a global using.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GlobalUsingsOnlyCodeFix))]
[Shared]
public sealed class GlobalUsingsOnlyCodeFix : CodeFixProvider
{
    #region Private Fields

    private const string GlobalUsingsFileName = "GlobalUsings.cs";

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

        var action = CodeAction.Create(
            "Move to GlobalUsings.cs",
            token => MoveToGlobalUsings(context.Document, directive, token),
            nameof(GlobalUsingsOnlyCodeFix));

        context.RegisterCodeFix(action, diagnostic);
    }

    #endregion

    #region Private Methods

    private static async Task<Solution> MoveToGlobalUsings(
        Document document,
        UsingDirectiveSyntax directive,
        CancellationToken token)
    {
        var line = $"global {directive.WithoutTrivia()}";
        var root = await document.GetSyntaxRootAsync(token);
        var withoutDirective = root!.RemoveNode(directive, SyntaxRemoveOptions.KeepNoTrivia)!;
        var solution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, withoutDirective);

        return await AddGlobalUsing(solution, document.Project.Id, line, token);
    }

    private static async Task<Solution> AddGlobalUsing(
        Solution solution,
        ProjectId projectId,
        string line,
        CancellationToken token)
    {
        var project = solution.GetProject(projectId)!;
        var existing = project.Documents.FirstOrDefault(candidate => candidate.Name == GlobalUsingsFileName);

        if (existing is null)
        {
            return solution.AddDocument(DocumentId.CreateNewId(projectId), GlobalUsingsFileName, $"{line}\n");
        }

        var text = await existing.GetTextAsync(token);

        if (text.ToString().Contains(line))
        {
            return solution;
        }

        var separator = text.Length == 0 || text.ToString().EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n";
        return solution.WithDocumentText(existing.Id, text.Replace(text.Length, 0, $"{separator}{line}\n"));
    }

    #endregion
}
