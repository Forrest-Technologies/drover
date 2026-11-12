namespace Drover.Analyzers.CodeFixes;

/// <summary>Rewrites a chain of + on strings as one interpolated string.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NoStringConcatenationCodeFix))]
[Shared]
public sealed class NoStringConcatenationCodeFix : CodeFixProvider
{
    #region Properties

    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.NoStringConcatenation];

    #endregion

    #region Public Methods

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics[0];

        if (root?.FindNode(diagnostic.Location.SourceSpan) is not BinaryExpressionSyntax concatenation)
        {
            return;
        }

        var action = CodeAction.Create(
            "Use string interpolation",
            token => Interpolate(context.Document, concatenation, token),
            nameof(NoStringConcatenationCodeFix));

        context.RegisterCodeFix(action, diagnostic);
    }

    #endregion

    #region Private Methods

    private static async Task<Document> Interpolate(
        Document document,
        BinaryExpressionSyntax concatenation,
        CancellationToken token)
    {
        var model = await document.GetSemanticModelAsync(token);
        var root = await document.GetSyntaxRootAsync(token);
        var builder = new StringBuilder("$\"");

        foreach (var part in FlattenOperands(concatenation, model!, token))
        {
            builder.Append(RenderPart(part));
        }

        builder.Append('"');

        var replacement = SyntaxFactory.ParseExpression(builder.ToString()).WithTriviaFrom(concatenation);
        return document.WithSyntaxRoot(root!.ReplaceNode(concatenation, replacement));
    }

    private static IEnumerable<ExpressionSyntax> FlattenOperands(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken token)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized
            && IsStringConcatenation(parenthesized.Expression, model, token))
        {
            return FlattenOperands(parenthesized.Expression, model, token);
        }

        if (expression is BinaryExpressionSyntax binary && IsStringConcatenation(binary, model, token))
        {
            return FlattenOperands(binary.Left, model, token).Concat(FlattenOperands(binary.Right, model, token));
        }

        return [expression];
    }

    private static bool IsStringConcatenation(ExpressionSyntax expression, SemanticModel model, CancellationToken token) =>
        expression.IsKind(SyntaxKind.AddExpression)
        && model.GetOperation(expression, token) is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Add,
            OperatorMethod: null,
            Type.SpecialType: SpecialType.System_String,
        };

    private static string RenderPart(ExpressionSyntax part)
    {
        if (part is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            && literal.Token.Text.StartsWith("\"", StringComparison.Ordinal))
        {
            return EscapeText(literal.Token.ValueText);
        }

        var inner = part is ConditionalExpressionSyntax or SwitchExpressionSyntax or AssignmentExpressionSyntax
            ? $"({part.WithoutTrivia()})"
            : part.WithoutTrivia().ToString();

        return $"{{{inner}}}";
    }

    private static string EscapeText(string text)
    {
        var quoted = SymbolDisplay.FormatLiteral(text, quote: true);
        return quoted.Substring(1, quoted.Length - 2).Replace("{", "{{").Replace("}", "}}");
    }

    #endregion
}
