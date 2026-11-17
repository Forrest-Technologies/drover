namespace Drover.Adapters.Conformance.Sandboxes;

/// <summary>The situations every sandbox adapter must handle. Offline, each has a fixture of the same name.</summary>
public static class SandboxScenarios
{
    #region Properties

    public const string CreatesWithMetadata = "creates_with_metadata";

    public const string KeepsAlive = "keeps_alive";

    public const string RunsACommand = "runs_a_command";

    public const string ExitsZero = "exits_zero";

    public const string RoundTripsBinary = "round_trips_binary";

    public const string ListsFiles = "lists_files";

    public const string DeniesNetwork = "denies_network";

    public const string PausesAndResumes = "pauses_and_resumes";

    public const string Destroys = "destroys";

    public const string RefusesAnotherGroupsHandle = "refuses_another_groups_handle";

    public const string RefusesAWrongToken = "refuses_a_wrong_token";

    public const string KillsOnCancel = "kills_on_cancel";

    public const string StopsAtTheTimeLimit = "stops_at_the_time_limit";

    #endregion
}
