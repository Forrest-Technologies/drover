namespace Drover.Adapters.Tests.Agents;

/// <summary>A draft, written, then held for a human's review, then published: the smallest workflow with a request in it.</summary>
public static class ReviewWorkflow
{
    #region Public Methods

    public static Workflow Build()
    {
        var writer = new Writer();
        var review = RequestPort.Create<ReviewRequest, bool>("Review");
        var publisher = new Publisher();

        return new WorkflowBuilder(writer)
            .AddEdge(writer, review)
            .AddEdge(review, publisher)
            .WithOutputFrom(publisher)
            .Build();
    }

    #endregion
}


public sealed record ReviewRequest(string Draft);


[SendsMessage(typeof(ReviewRequest))]
public sealed class Writer() : Executor<string>("Writer")
{
    #region Public Methods

    public override ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        context.SendMessageAsync(new ReviewRequest($"{message}, written"), cancellationToken: cancellationToken);

    #endregion
}


[YieldsOutput(typeof(string))]
public sealed class Publisher() : Executor<bool>("Publisher")
{
    #region Public Methods

    public override ValueTask HandleAsync(bool message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        context.YieldOutputAsync(message ? "published: draft, written" : "rejected", cancellationToken);

    #endregion
}
