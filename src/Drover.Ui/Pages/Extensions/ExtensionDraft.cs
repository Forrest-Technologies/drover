namespace Drover.Ui.Pages.Extensions;

/// <summary>
/// An extension while a person writes it: the fields of the editor's form. It becomes a package to be shown as
/// text, checked, or imported, and is filled from one when a bot or a text is opened.
/// </summary>
public sealed class ExtensionDraft
{
    #region Properties

    public string Name { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    /// <summary>The person typed an id themselves, so it no longer follows the name.</summary>
    public bool IdIsOwn { get; set; }

    public string Description { get; set; } = string.Empty;

    public string BotName { get; set; } = string.Empty;

    public string BotDescription { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public string SuggestedModel { get; set; } = string.Empty;

    /// <summary>The extension adds a bot. Off for one that only names a tool server.</summary>
    public bool AddsBot { get; set; } = true;

    /// <summary>The extension names a tool server for a person to connect.</summary>
    public bool NamesServer { get; set; }

    public string ServerName { get; set; } = string.Empty;

    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>How Drover proves who is calling: <c>none</c>, <c>header</c>, or <c>oauth</c>.</summary>
    public string ServerAuth { get; set; } = "none";

    public string HeaderName { get; set; } = string.Empty;

    /// <summary>The suggested tools as the form holds them: one a line, the tool's name, then a colon and why.</summary>
    public string SuggestedTools { get; set; } = string.Empty;

    #endregion

    #region Public Methods

    public static ExtensionDraft From(ExtensionPackage package)
    {
        var bot = package.Contents.OfType<BotTemplate>().FirstOrDefault();
        var server = package.Contents.OfType<ConnectionRecipe>().FirstOrDefault();
        return new()
        {
            AddsBot = bot is not null,
            NamesServer = server is not null,
            ServerName = server?.Name ?? string.Empty,
            ServerAddress = server?.Url ?? string.Empty,
            ServerAuth = server?.Auth ?? "none",
            HeaderName = server?.HeaderName ?? string.Empty,
            SuggestedTools = string.Join('\n', (server?.SuggestedTools ?? []).Select(tool => string.IsNullOrWhiteSpace(tool.Why) ? tool.Name : $"{tool.Name}: {tool.Why}")),
            Name = package.Name,
            Id = package.Id,
            IdIsOwn = true,
            Description = package.Description,
            BotName = bot?.Name ?? string.Empty,
            BotDescription = bot?.Description ?? string.Empty,
            Instructions = bot?.Instructions ?? string.Empty,
            SuggestedModel = bot?.SuggestedModel ?? string.Empty,
        };
    }

    /// <summary>Sets the name, and the id with it until the person has typed an id of their own.</summary>
    public void Rename(string name)
    {
        Name = name;

        if (!IdIsOwn)
        {
            Id = string.IsNullOrWhiteSpace(name) ? string.Empty : ExtensionFile.IdFrom(name);
        }
    }

    public void ChooseId(string id)
    {
        Id = id;
        IdIsOwn = !string.IsNullOrWhiteSpace(id);
    }

    public ExtensionPackage ToPackage()
    {
        List<ExtensionContent> contents = [];

        if (AddsBot)
        {
            contents.Add(new BotTemplate(BotName.Trim(), Instructions)
            {
                Description = BotDescription,
                SuggestedModel = string.IsNullOrWhiteSpace(SuggestedModel) ? null : SuggestedModel.Trim(),
            });
        }

        if (NamesServer)
        {
            contents.Add(new ConnectionRecipe(ServerName.Trim(), ServerAddress.Trim())
            {
                Auth = ServerAuth,
                HeaderName = ServerAuth is "header" ? HeaderName.Trim() : null,
                SuggestedTools = [.. ReadSuggestedTools()],
            });
        }

        return new(ExtensionPackage.CurrentVersion, Id.Trim(), Name.Trim()) { Description = Description, Contents = contents };
    }

    #endregion

    #region Private Methods

    /// <summary>Each line that holds anything, split at its first colon into the tool's name and why.</summary>
    private IEnumerable<SuggestedTool> ReadSuggestedTools() =>
        SuggestedTools.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Select(parts => new SuggestedTool(parts[0], parts.Length > 1 ? parts[1] : string.Empty));

    #endregion
}

/// <summary>
/// Carries a file's text from the editor to the import on the extensions screen, once. Kept for the visit only:
/// the import reads it and it is gone.
/// </summary>
public sealed class ExtensionHandoff
{
    #region Private Fields

    private string? text;

    #endregion

    #region Public Methods

    public void Give(string file) => text = file;

    public string? Take()
    {
        var taken = text;
        text = null;
        return taken;
    }

    #endregion
}
