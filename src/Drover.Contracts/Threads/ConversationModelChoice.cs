namespace Drover.Contracts.Threads;

/// <summary>
/// The models a conversation may answer with. <see cref="BotModel"/> is the bot's own model, named
/// <see cref="BotModelName"/>; it answers while <see cref="Chosen"/> is null. <see cref="Chosen"/> is the model the
/// conversation answers with in its place. <see cref="Models"/> are the models it may choose: those on the bot's model
/// connection that the space may use, which can include the bot's own.
/// </summary>
public sealed record ConversationModelChoice(
    string BotModel,
    string BotModelName,
    string? Chosen,
    IReadOnlyList<ConversationModelOption> Models);

/// <summary>One model a conversation may choose: its id and the name people read.</summary>
public sealed record ConversationModelOption(string Model, string Name);

/// <summary>The model the conversation answers with from its next run. Null, or the bot's own model, goes back to the bot's.</summary>
public sealed record ChooseModelRequest(string? Model);
