namespace Drover.Client.Runs;

/// <summary>
/// Turns a run's events into the view a screen renders. It is a pure function: the same events in the same order
/// give the same view, and an event at or below <see cref="RunView.LastSeq"/> changes nothing, so feeding any prefix
/// of a log and then the rest, or the same event twice after a reconnect, ends in the same place as feeding it all
/// at once.
/// </summary>
public static class RunReducer
{
    #region Public Methods

    public static RunView Apply(RunView view, RunEventResponse stored)
    {
        if (stored.Seq <= view.LastSeq)
        {
            return view;
        }

        var seen = view with { LastSeq = stored.Seq };

        // A stream carries a whole run tree. Events of other runs in it move the cursor and nothing else here.
        return stored.RunId == view.RunId ? ApplyOwn(seen, stored) : seen;
    }

    public static RunView Apply(RunView view, IEnumerable<RunEventResponse> events) => events.Aggregate(view, Apply);

    #endregion

    #region Private Methods

    private static RunView ApplyOwn(RunView view, RunEventResponse stored) => stored.Type switch
    {
        RunEventTypes.RunQueued or RunEventTypes.RunStarted or RunEventTypes.RunWaiting or RunEventTypes.RunCompleted
            or RunEventTypes.RunFailed or RunEventTypes.RunCancelled => ChangeStatus(view, RunEventTypes.Read<RunStatusChanged>(stored)),
        RunEventTypes.StepStarted => StartMessage(view, RunEventTypes.Read<StepChanged>(stored)),
        RunEventTypes.StepDiscarded => DiscardMessage(view, RunEventTypes.Read<StepChanged>(stored)),
        RunEventTypes.MessageDelta => AppendDelta(view, RunEventTypes.Read<MessageDelta>(stored)),
        RunEventTypes.MessageCompleted => CompleteMessage(view, RunEventTypes.Read<MessageCompleted>(stored).Message),
        RunEventTypes.MessageDelivered => CompleteMessage(view, RunEventTypes.Read<MessageDelivered>(stored).Message),
        RunEventTypes.ToolRequested => RequestTool(view, RunEventTypes.Read<ToolRequested>(stored)),
        RunEventTypes.ToolApprovalRequested => AwaitApproval(view, RunEventTypes.Read<ToolApprovalRequested>(stored)),
        RunEventTypes.ToolApproved => ChangeTool(view, RunEventTypes.Read<ToolDecided>(stored).ToolCallId, tool => tool with { State = "approved" }),
        RunEventTypes.ToolDenied => ChangeTool(view, RunEventTypes.Read<ToolDecided>(stored).ToolCallId, tool => tool with { State = "denied" }),
        RunEventTypes.ToolStarted => ChangeTool(view, RunEventTypes.Read<ToolStarted>(stored).ToolCallId, tool => tool with { State = "running" }),
        RunEventTypes.ToolCompleted => CompleteTool(view, RunEventTypes.Read<ToolCompleted>(stored)),
        RunEventTypes.UsageRecorded => AddUsage(view, RunEventTypes.Read<UsageRecorded>(stored)),

        // A type this client does not know yet is skipped, so an older client keeps working against a newer server.
        _ => view,
    };

    private static RunView ChangeStatus(RunView view, RunStatusChanged changed) =>
        view with
        {
            Status = changed.Status,
            Reason = changed.Reason,
            Error = changed.Error,
            Attempt = changed.Attempt,
            Usage = changed.Usage ?? view.Usage,
        };

    private static RunView StartMessage(RunView view, StepChanged step) =>
        view.Messages.Any(message => message.Id == step.MessageId)
            ? view
            : view with { Messages = view.Messages.Add(new MessageView(step.MessageId, "assistant", [], IsStreaming: true, step.Step)) };

    /// <summary>The step was cut off and its message never stored, so its partial output goes.</summary>
    private static RunView DiscardMessage(RunView view, StepChanged step) =>
        view with { Messages = view.Messages.RemoveAll(message => message.Id == step.MessageId && message.IsStreaming) };

    private static RunView AppendDelta(RunView view, MessageDelta delta)
    {
        var index = view.Messages.FindIndex(message => message.Id == delta.MessageId);
        var message = index < 0 ? new MessageView(delta.MessageId, "assistant", [], IsStreaming: true, null) : view.Messages[index];

        if (!message.IsStreaming)
        {
            return view;
        }

        var parts = message.Parts;

        while (parts.Count <= delta.PartIndex)
        {
            parts = parts.Add(new PartView(delta.Kind, string.Empty));
        }

        var part = parts[delta.PartIndex];
        var grown = message with { Parts = parts.SetItem(delta.PartIndex, part with { Text = $"{part.Text}{delta.Text}" }) };
        return view with { Messages = index < 0 ? view.Messages.Add(grown) : view.Messages.SetItem(index, grown) };
    }

    private static RunView CompleteMessage(RunView view, MessageResponse stored)
    {
        var index = view.Messages.FindIndex(message => message.Id == stored.Id);
        var completed = MessageView.From(stored) with { Step = index < 0 ? null : view.Messages[index].Step };
        return view with { Messages = index < 0 ? view.Messages.Add(completed) : view.Messages.SetItem(index, completed) };
    }

    private static RunView RequestTool(RunView view, ToolRequested requested) =>
        view.ToolCalls.Any(tool => tool.ToolCallId == requested.ToolCallId)
            ? view
            : view with
            {
                ToolCalls = view.ToolCalls.Add(new ToolCallView(
                    requested.ToolCallId,
                    requested.ToolName,
                    requested.Title,
                    requested.Arguments,
                    requested.Effect,
                    "requested")),
            };

    private static RunView AwaitApproval(RunView view, ToolApprovalRequested approval) =>
        ChangeTool(view, approval.ToolCallId, tool => tool with { State = "awaiting_approval", RequestId = approval.RequestId });

    private static RunView CompleteTool(RunView view, ToolCompleted completed) =>
        ChangeTool(
            view,
            completed.ToolCallId,
            tool => tool with
            {
                State = completed.Outcome,
                IsError = completed.IsError,
                Preview = completed.Preview,
                DurationMs = completed.DurationMs,
            });

    private static RunView ChangeTool(RunView view, Guid toolCallId, Func<ToolCallView, ToolCallView> change)
    {
        var index = view.ToolCalls.FindIndex(tool => tool.ToolCallId == toolCallId);
        return index < 0 ? view : view with { ToolCalls = view.ToolCalls.SetItem(index, change(view.ToolCalls[index])) };
    }

    private static RunView AddUsage(RunView view, UsageRecorded used) =>
        view with
        {
            Usage = new UsageTotals(
                view.Usage.Input + used.Input,
                view.Usage.Output + used.Output,
                view.Usage.Cached + used.Cached,
                view.Usage.Cost + used.Cost),
        };

    #endregion
}
