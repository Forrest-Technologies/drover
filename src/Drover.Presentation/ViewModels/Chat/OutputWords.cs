namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What a tool printed, in words: error output is tagged with the name of its stream, and also colored.</summary>
public static class OutputWords
{
    /// <summary>The stream a tool writes its errors to, which is also the tag its lines carry.</summary>
    public const string ErrorStream = "stderr";
}
