namespace Drover.Contracts.Models;

/// <summary>
/// Input only: whether a space admin says a model of a compatible connection reads pictures. The model is named in
/// the body because a model's ID can hold slashes. False clears the mark.
/// </summary>
public sealed record SetModelVisionRequest(string? Model, bool ReadsImages);
