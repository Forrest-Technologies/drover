namespace Drover.Engine.Audit;

/// <summary>The kinds of thing an audit entry can be about, as the log spells them.</summary>
public static class AuditTargets
{
    #region Properties

    public const string Bot = "bot";

    public const string Extension = "extension";

    public const string Project = "project";

    public const string SandboxSecret = "sandbox_secret";

    public const string AllowAllWindow = "allow_all_window";

    public const string Space = "space";

    public const string ModelConnection = "model_connection";

    public const string DecisionModel = "decision_model";

    #endregion
}
