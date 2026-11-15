namespace Drover.Ui.Starters;

/// <summary>Words for a routine that starts when another system calls it.</summary>
public static class RoutineCallWords
{
    /// <summary>Today's calls from stored counts: "No calls today", "12 calls today", "12 calls today, 2 refused".</summary>
    public static string Today(int received, int refused) => (received, refused) switch
    {
        (0, 0) => "No calls today.",
        (_, 0) => $"{Words.Count(received, "call")} today.",
        _ => $"{Words.Count(received, "call")} today, {refused} refused.",
    };
}
