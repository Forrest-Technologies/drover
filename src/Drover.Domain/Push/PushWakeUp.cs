namespace Drover.Domain.Push;

/// <summary>What a device is woken for: one of <see cref="PushKinds"/>, and the thread it is about. Nothing else.</summary>
public sealed record PushWakeUp(string Kind, Guid ThreadId);
