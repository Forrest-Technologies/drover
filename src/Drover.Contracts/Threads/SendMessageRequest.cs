namespace Drover.Contracts.Threads;

public sealed record SendMessageRequest(string? Text);

/// <summary>The stored message and the run that will answer it. The client opens the run's event stream next.</summary>
public sealed record SendMessageResponse(MessageResponse Message, RunSummary Run);
