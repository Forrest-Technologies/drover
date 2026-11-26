namespace Drover.Engine.Extensions;

/// <summary>A theme in an extension: the words it is chosen by, and its file as text.</summary>
public sealed record PackagedTheme(string Id, string Name, string Intent, string File);

public static class ExtensionViews
{
    #region Public Methods

    /// <summary>The rule as the body a bot's rule is added with. It is on when added, and goes last.</summary>
    public static BotRuleBody ToBody(this RuleTemplate rule) => new(rule.Name, true, rule.Checkpoint, rule.Effect, rule.Conditions);

    /// <summary>The themes the package holds. Every one passed the theme checks when the file was read.</summary>
    public static IReadOnlyList<PackagedTheme> Themes(this ExtensionPackage package) =>
    [
        .. package.Contents.OfType<ThemePart>().Select(part => new PackagedTheme(
            part.Theme.GetProperty("id").GetString()!,
            part.Theme.GetProperty("name").GetString()!,
            part.Theme.GetProperty("intent").GetString()!,
            part.Theme.GetRawText())),
    ];

    public static ExtensionParts ToParts(this ExtensionPackage package) => new()
    {
        Settings = package.Settings,
        Themes = [.. package.Themes().Select(theme => new ExtensionTheme(theme.Id, theme.Name, theme.Intent))],
        Rules = [.. package.Contents.OfType<RuleTemplate>().Select(ToBody)],
    };

    /// <summary>
    /// The tool server as a person reads it. <paramref name="values"/> are the settings filled in so far: while the
    /// address still holds one, where calls go is not known, and the host is shown as the file writes it.
    /// </summary>
    public static ExtensionConnectionPreview ToPreview(this ConnectionRecipe recipe, IReadOnlyDictionary<string, string> values, bool available)
    {
        var url = ExtensionPackages.Fill(recipe.Url.Trim(), values);
        var address = ExtensionPackages.SettingsIn(url).Count == 0 && Uri.TryCreate(url, UriKind.Absolute, out var whole) ? whole : null;

        return new(recipe.Name.Trim(), url, address?.Host ?? WrittenHost(url), recipe.Auth, recipe.HeaderName, address is not null && ExtensionPackages.IsLocal(address), available)
        {
            DependsOnSettings = address is null,
            AsksForHeader = recipe is { Auth: "header", HeaderTemplate: null },
            SuggestedTools = recipe.SuggestedTools,
        };
    }

    public static ExtensionConnectionSummary ToSummary(this ExtensionConnectionPreview preview) =>
        new(preview.Name, preview.Url, preview.Host, preview.Auth, preview.HeaderName, preview.IsLocal, preview.Available)
        {
            DependsOnSettings = preview.DependsOnSettings,
            AsksForHeader = preview.AsksForHeader,
        };

    public static ExtensionSummary ToSummary(this ExtensionState state) =>
        new(state.Install.Id, state.Install.PackageId, state.Install.Name, state.Install.Description, state.Install.InstalledAt)
        {
            Connection = state.Connection,
            Parts = state.Package?.ToParts() ?? new(),
            FilledSettings = state.Install.Settings,
        };

    #endregion

    #region Private Methods

    /// <summary>What stands where the host goes in an address that still holds a setting: the text between <c>://</c> and the path.</summary>
    private static string WrittenHost(string url)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal) is >= 0 and var scheme ? scheme + 3 : 0;
        var end = url.IndexOf('/', start);
        return end < 0 ? url[start..] : url[start..end];
    }

    #endregion
}
