using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Domain;
using MassTransit;

namespace DocFlow.Infrastructure.Messaging;

public abstract class WorkflowActivity<TMessage> : IStateMachineActivity<DocumentState, TMessage>
    where TMessage : class
{
    public void Probe(ProbeContext context) => context.CreateScope(GetType().Name);

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<DocumentState, TMessage> context, IBehavior<DocumentState, TMessage> next)
    {
        await ExecuteAsync(context);
        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<DocumentState, TMessage, TException> context,
        IBehavior<DocumentState, TMessage> next)
        where TException : Exception => next.Faulted(context);

    protected abstract Task ExecuteAsync(BehaviorContext<DocumentState, TMessage> context);
}

public sealed class StartExtractionActivity(DocumentWorkflow workflow) : WorkflowActivity<DocumentReceived>
{
    protected override async Task ExecuteAsync(BehaviorContext<DocumentState, DocumentReceived> context)
    {
        await workflow.StartExtractionAsync(context.Message.DocumentId, context.CancellationToken);
        context.Saga.LastStatus = nameof(DocumentStatus.Extracting);
    }
}

public sealed class RecordExtractionActivity(DocumentWorkflow workflow) : WorkflowActivity<DocumentExtracted>
{
    protected override async Task ExecuteAsync(BehaviorContext<DocumentState, DocumentExtracted> context)
    {
        var message = context.Message;
        var result = new ExtractionResult(message.Invoice.ToDomain(), message.Confidence, message.Method);
        await workflow.RecordExtractionAsync(message.DocumentId, result, context.CancellationToken);
        context.Saga.LastStatus = nameof(DocumentStatus.Validating);
    }
}

public sealed class FailExtractionActivity(DocumentWorkflow workflow) : WorkflowActivity<DocumentExtractionFailed>
{
    protected override async Task ExecuteAsync(BehaviorContext<DocumentState, DocumentExtractionFailed> context)
    {
        await workflow.FailExtractionAsync(context.Message.DocumentId, context.Message.Reason, context.CancellationToken);
        context.Saga.LastStatus = nameof(DocumentStatus.NeedsReview);
    }
}

public sealed class CompleteValidationActivity(DocumentWorkflow workflow) : WorkflowActivity<DocumentValidated>
{
    protected override async Task ExecuteAsync(BehaviorContext<DocumentState, DocumentValidated> context)
    {
        var status = await workflow.CompleteValidationAsync(
            context.Message.DocumentId,
            context.Message.Issues.ToDomain(),
            context.CancellationToken);
        context.Saga.LastStatus = status.ToString();
    }
}
