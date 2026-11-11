namespace Drover.Analyzers.CodeFixes;

/// <summary>Renames a method to drop the Async suffix, along with every reference to it.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NoAsyncSuffixCodeFix))]
[Shared]
public sealed class NoAsyncSuffixCodeFix : CodeFixProvider
{
    #region Properties

    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.NoAsyncSuffix];

    #endregion

    #region Public Methods

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];

        if (root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent is not MethodDeclarationSyntax method)
        {
            return;
        }

        var newName = method.Identifier.Text.Substring(0, method.Identifier.Text.Length - "Async".Length);
        var action = CodeAction.Create(
            $"Rename to '{newName}'",
            token => RenameMethod(context.Document, method, newName, token),
            nameof(NoAsyncSuffixCodeFix));

        context.RegisterCodeFix(action, diagnostic);
    }

    #endregion

    #region Private Methods

    private static async Task<Solution> RenameMethod(
        Document document,
        MethodDeclarationSyntax method,
        string newName,
        CancellationToken token)
    {
        var model = await document.GetSemanticModelAsync(token);
        var symbol = model?.GetDeclaredSymbol(method, token);

        if (symbol is null)
        {
            return document.Project.Solution;
        }

        return await Renamer.RenameSymbolAsync(document.Project.Solution, symbol, new SymbolRenameOptions(), newName, token);
    }

    #endregion
}
