namespace Drover.Analyzers.Rules;

/// <summary>
/// DRV0003: a non-trivial type groups its members in regions, in the standard order, with a blank line after
/// each opening tag and before each closing tag.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegionsRequiredAnalyzer : DiagnosticAnalyzer
{
    #region Private Fields

    private const int TrivialMemberLimit = 3;

    private const int DescriptiveGroupRank = 4;

    private const string StandardOrder =
        "use Private Fields, Constructors, Properties, Public Methods or Interface Implementations, then other groups";

    private static readonly Dictionary<string, int> standardRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Private Fields"] = 0,
        ["Constructors"] = 1,
        ["Properties"] = 2,
        ["Public Methods"] = 3,
        ["Interface Implementations"] = 3,
    };

    #endregion

    #region Properties

    public static DiagnosticDescriptor Rule { get; } = RuleDescriptors.Style(
        DiagnosticIds.RegionsRequired,
        "Regions required",
        "{0}",
        "A type with more than three members, or more than one kind of member, groups them in regions in the order "
        + "Private Fields, Constructors, Properties, Public Methods or Interface Implementations, then private "
        + "methods or descriptively named groups. A blank line follows each opening tag and precedes each closing tag.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    #endregion

    #region Public Methods

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeType,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    #endregion

    #region Analysis

    private static void AnalyzeType(SyntaxNodeAnalysisContext context)
    {
        var type = (TypeDeclarationSyntax)context.Node;

        if (IsPositionalRecord(type))
        {
            return;
        }

        var regions = FindRegions(type);

        if (regions.Count == 0 && IsNonTrivial(type))
        {
            Report(context, type.Identifier.GetLocation(), $"'{type.Identifier.Text}' needs regions: {StandardOrder}");
            return;
        }

        CheckRegionOrder(context, regions);
        CheckRegionPadding(context, regions);

        if (IsNonTrivial(type))
        {
            CheckMembersAreInRegions(context, type, regions);
        }
    }

    private static bool IsPositionalRecord(TypeDeclarationSyntax type) =>
        type is RecordDeclarationSyntax { ParameterList: not null };

    private static bool IsNonTrivial(TypeDeclarationSyntax type) =>
        type.Members.Count > TrivialMemberLimit
        || type.Members.Select(KindOf).Distinct().Count() > 1;

    private static MemberKind KindOf(MemberDeclarationSyntax member) =>
        member switch
        {
            FieldDeclarationSyntax => MemberKind.Field,
            ConstructorDeclarationSyntax or DestructorDeclarationSyntax => MemberKind.Constructor,
            PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax or EventFieldDeclarationSyntax
                => MemberKind.Property,
            BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => MemberKind.NestedType,
            _ => MemberKind.Method,
        };

    private static void Report(SyntaxNodeAnalysisContext context, Location location, string message) =>
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, message));

    #endregion

    #region Region Discovery

    private static List<Region> FindRegions(TypeDeclarationSyntax type)
    {
        var memberSpans = type.Members.Select(member => member.Span).ToList();
        var body = TextSpan.FromBounds(type.OpenBraceToken.Span.End, type.CloseBraceToken.SpanStart);

        return [..type.DescendantTrivia(body)
            .Where(trivia => trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            .Select(trivia => (RegionDirectiveTriviaSyntax)trivia.GetStructure()!)
            .Where(open => !memberSpans.Any(span => span.Contains(open.SpanStart)))
            .Select(open => new Region(open, open.GetRelatedDirectives().OfType<EndRegionDirectiveTriviaSyntax>().FirstOrDefault()))
            .Where(region => region.Close is not null)];
    }

    #endregion

    #region Checks

    private static void CheckRegionOrder(SyntaxNodeAnalysisContext context, List<Region> regions)
    {
        var highestRank = -1;

        foreach (var region in regions)
        {
            var rank = RankOf(region.Name);

            if (rank < highestRank)
            {
                Report(context, region.Open.GetLocation(), $"Region '{region.Name}' is out of order: {StandardOrder}");
                continue;
            }

            highestRank = rank;
        }
    }

    private static int RankOf(string name) =>
        standardRanks.TryGetValue(name, out var rank) ? rank : DescriptiveGroupRank;

    private static void CheckRegionPadding(SyntaxNodeAnalysisContext context, List<Region> regions)
    {
        foreach (var region in regions)
        {
            if (!IsBlankLineAfter(region.Open))
            {
                Report(context, region.Open.GetLocation(), $"Put a blank line after '#region {region.Name}'");
            }

            if (region.Close is not null && !IsBlankLineBefore(region.Close))
            {
                Report(context, region.Close.GetLocation(), $"Put a blank line before '#endregion' of '{region.Name}'");
            }
        }
    }

    private static bool IsBlankLineAfter(DirectiveTriviaSyntax directive)
    {
        var lines = directive.SyntaxTree.GetText().Lines;
        var next = directive.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        return next < lines.Count && string.IsNullOrWhiteSpace(lines[next].ToString());
    }

    private static bool IsBlankLineBefore(DirectiveTriviaSyntax directive)
    {
        var lines = directive.SyntaxTree.GetText().Lines;
        var previous = directive.GetLocation().GetLineSpan().StartLinePosition.Line - 1;
        return previous >= 0 && string.IsNullOrWhiteSpace(lines[previous].ToString());
    }

    private static void CheckMembersAreInRegions(
        SyntaxNodeAnalysisContext context,
        TypeDeclarationSyntax type,
        List<Region> regions)
    {
        foreach (var member in type.Members.Where(member => !regions.Any(region => region.Contains(member))))
        {
            Report(context, member.GetFirstToken().GetLocation(), "Move this member into a region");
        }
    }

    #endregion

    #region Nested Types

    private enum MemberKind
    {
        Field,
        Constructor,
        Property,
        Method,
        NestedType,
    }

    private sealed record Region(RegionDirectiveTriviaSyntax Open, EndRegionDirectiveTriviaSyntax? Close)
    {
        public string Name => Open.EndOfDirectiveToken.LeadingTrivia.ToString().Trim();

        public bool Contains(MemberDeclarationSyntax member) =>
            Open.SpanStart < member.SpanStart && Close is not null && member.Span.End < Close.SpanStart;
    }

    #endregion
}
