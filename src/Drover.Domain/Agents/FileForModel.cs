namespace Drover.Domain.Agents;

/// <summary>
/// What a model is given in place of a file a person attached: always a line of text that says what the file is
/// and, for text, its content; for a picture the model reads, the picture as well. The text is the person's own
/// input, as their message is: it carries no more authority than they do.
/// </summary>
public sealed record FileForModel(Guid FileId, string Text, byte[]? Image = null, string? MediaType = null);
