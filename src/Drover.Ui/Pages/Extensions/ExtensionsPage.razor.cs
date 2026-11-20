namespace Drover.Ui.Pages.Extensions;

/// <summary>
/// The extensions of the space. Its job: bring in a bot someone shared, after reading exactly what it will be told,
/// and turn it on.
/// </summary>
public sealed partial class ExtensionsPage(DroverApi api, SessionState session, ExtensionHandoff handoff, ILogger<ExtensionsPage> logger)
{
    #region Private Fields

    private List<ExtensionSummary>? extensions;

    private ExtensionPreview? preview;

    private string? packageText;

    private Guid? busy;

    private string? loadProblem;

    private string? importProblem;

    private string? notice;

    private bool noticeIsProblem;

    private bool importing;

    private bool importBusy;

    #endregion

    #region Properties

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    private bool CanEdit => session.Space?.Role is "owner" or "admin";

    #endregion

    #region Overrides

    protected override async Task OnInitializedAsync()
    {
        if (await session.Load() is not null)
        {
            await Load();

            // A file handed over by the editor is reviewed here like any other, before anything is added.
            if (CanEdit && loadProblem is null && handoff.Take() is { } handed)
            {
                StartImporting();
                packageText = handed;
                await Read(handed);
            }
        }
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        loadProblem = null;

        try
        {
            extensions = await api.ListExtensions(SpaceId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsPage));
            loadProblem = Failures.Sentence(exception);
        }
    }

    private void StartImporting()
    {
        importing = true;
        preview = null;
        packageText = null;
        importProblem = null;
        notice = null;
    }

    private void StopImporting() => importing = false;

    /// <summary>Asks the server what the file would add. Nothing is added by reading it.</summary>
    private Task Read(string text) =>
        WhileImporting(async () =>
        {
            preview = null;
            packageText = text;
            preview = await api.PreviewExtension(SpaceId, text);
        });

    private Task Import() =>
        WhileImporting(async () =>
        {
            var added = await api.ImportExtension(SpaceId, packageText!);
            extensions!.Insert(0, added);
            importing = false;
            (notice, noticeIsProblem) = (ExtensionWords.Imported(added), false);
        });

    private async Task WhileImporting(Func<Task> work)
    {
        importBusy = true;
        importProblem = null;

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsPage));
            importProblem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            importBusy = false;
        }
    }

    /// <summary>Shows an extension as it is after one of its steps.</summary>
    private void Replace(ExtensionSummary changed)
    {
        if (extensions!.FindIndex(listed => listed.Id == changed.Id) is >= 0 and var index)
        {
            extensions[index] = changed;
        }
    }

    private async Task TurnOn(ExtensionSummary extension)
    {
        busy = extension.Id;
        notice = null;

        try
        {
            var bot = await api.ResumeBot(SpaceId, extension.BotId!.Value);
            extensions![extensions.IndexOf(extension)] = extension with { BotStatus = bot.Status };
            (notice, noticeIsProblem) = ($"{bot.Name} is on.", false);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsPage));
            (notice, noticeIsProblem) = (Failures.Sentence(exception, "Check that the server is running."), true);
        }
        finally
        {
            busy = null;
        }
    }

    #endregion
}
