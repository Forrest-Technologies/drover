namespace Drover.Presentation.ViewModels;

/// <summary>What the Model section says about a model that reads the screen for a bot that cannot see.</summary>
public enum ReaderShows
{
    /// <summary>The models that read images are not known yet.</summary>
    NotKnown,

    /// <summary>No model of the space reads images, and none is set.</summary>
    NoneToChoose,

    /// <summary>A list to choose the reader from.</summary>
    Choice,
}
