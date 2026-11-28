namespace Drover.Presentation.ViewModels.Chat;

/// <summary>One model the conversation may choose, with its catalog label and provider namespace when supplied.</summary>
public sealed record ModelOption(string Value, string Label)
{
    #region Properties

    public string? Provider => Value.IndexOf('/') is var separator && separator > 0 ? Value[..separator] : null;

    #endregion
}
