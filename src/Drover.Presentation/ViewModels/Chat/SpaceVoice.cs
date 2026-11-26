namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What speech can use in the signed-in person's space: which models listen, speak and hold a live call. It is read
/// from the server once for a space and kept, and read again after the space's model connections changed. Every
/// control of voice asks here whether it has anything to offer.
/// </summary>
public sealed class SpaceVoice(DroverApi api, SessionState session, ILogger<SpaceVoice> logger)
{
    #region Private Fields

    private Guid? loadedFor;

    private Task? loading;

    #endregion

    #region Properties

    /// <summary>The space's options, or null until they have been read, and when they could not be.</summary>
    public VoiceOptionsResponse? Options { get; private set; }

    /// <summary>True when a model can write down a recording.</summary>
    public bool CanListen => Options?.Listen.Selected is not null;

    /// <summary>True when a model can read text aloud.</summary>
    public bool CanSpeak => Options?.Speak.Selected is not null;

    /// <summary>True when a model can hold a live call.</summary>
    public bool HasLive => Options?.Live.Selected is not null;

    /// <summary>Raised when the options were read or dropped.</summary>
    public event Action? Changed;

    #endregion

    #region Public Methods

    /// <summary>Reads the options unless they are already known for this space. A failure leaves voice not offered.</summary>
    public Task Load()
    {
        if (session.Space?.Id is not { } space)
        {
            return Task.CompletedTask;
        }

        return loadedFor == space ? loading ?? Task.CompletedTask : loading = Read(space);
    }

    /// <summary>The space's model connections changed: what is known is out of date, and is read again now.</summary>
    public Task Refresh()
    {
        loadedFor = null;
        return Load();
    }

    /// <summary>Takes options the server just answered with, after a save.</summary>
    public void Take(VoiceOptionsResponse options)
    {
        loadedFor = session.Space?.Id;
        Options = options;
        Changed?.Invoke();
    }

    #endregion

    #region Private Methods

    private async Task Read(Guid space)
    {
        loadedFor = space;

        try
        {
            Options = await api.GetVoiceOptions(space);
        }
        catch (Exception failure) when (Failures.IsReportable(failure))
        {
            logger.LogWarning(failure, "The voice options of the space could not be read");
            Options = null;
            loadedFor = null;
        }

        Changed?.Invoke();
    }

    #endregion
}
