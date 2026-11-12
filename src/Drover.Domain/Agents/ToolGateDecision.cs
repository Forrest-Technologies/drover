namespace Drover.Domain.Agents;

public enum ToolGateVerdict
{
    Run,
    Ask,
    Refuse,
}


/// <summary>What the tool gate decided for one call. <see cref="Reason"/> is shown to the model on a refusal.</summary>
public sealed record ToolGateDecision(ToolGateVerdict Verdict, string? Reason = null)
{
    #region Properties

    public static ToolGateDecision Run { get; } = new(ToolGateVerdict.Run);

    public static ToolGateDecision Ask { get; } = new(ToolGateVerdict.Ask);

    #endregion

    #region Public Methods

    public static ToolGateDecision Refuse(string reason) => new(ToolGateVerdict.Refuse, reason);

    #endregion
}
