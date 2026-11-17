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

    #endregion

    #region Public Methods

    public static ExtensionDraft From(ExtensionPackage package)
    {
        var bot = package.Contents.OfType<BotTemplate>().FirstOrDefault();
        return new()
        {
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

    public ExtensionPackage ToPackage() =>
        new(ExtensionPackage.CurrentVersion, Id.Trim(), Name.Trim())
        {
            Description = Description,
            Contents =
            [
                new BotTemplate(BotName.Trim(), Instructions)
                {
                    Description = BotDescription,
                    SuggestedModel = string.IsNullOrWhiteSpace(SuggestedModel) ? null : SuggestedModel.Trim(),
                },
            ],
        };

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
