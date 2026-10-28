using DocFlow.Contracts;
using DocFlow.Domain;
using MassTransit;

namespace DocFlow.Infrastructure.Messaging;

public sealed class DocumentStateMachine : MassTransitStateMachine<DocumentState>
{
    public DocumentStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => Received, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => Extracted, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => ExtractionFailed, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => Validated, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => Reviewed, x => x.CorrelateById(c => c.Message.DocumentId));

        Initially(
            When(Received)
                .Then(c => c.Saga.StartedAt = DateTimeOffset.UtcNow)
                .Activity(x => x.OfType<StartExtractionActivity>())
                .Send(
                    new Uri($"queue:{Queues.ExtractDocument}"),
                    c => new ExtractDocument(c.Message.DocumentId, c.Message.BlobName))
                .TransitionTo(Extracting));

        During(
            Extracting,
            When(Extracted)
                .Activity(x => x.OfType<RecordExtractionActivity>())
                .Send(
                    new Uri($"queue:{Queues.ValidateDocument}"),
                    c => new ValidateDocument(c.Message.DocumentId, c.Message.Invoice))
                .TransitionTo(Validating),
            When(ExtractionFailed)
                .Activity(x => x.OfType<FailExtractionActivity>())
                .TransitionTo(AwaitingReview));

        During(
            Validating,
            When(Validated)
                .Activity(x => x.OfType<CompleteValidationActivity>())
                .IfElse(
                    c => c.Saga.LastStatus == nameof(DocumentStatus.NeedsReview),
                    review => review.TransitionTo(AwaitingReview),
                    done => done.Finalize()));

        During(AwaitingReview, When(Reviewed).Finalize());

        During(Extracting, Ignore(Validated), Ignore(Reviewed));
        During(Validating, Ignore(Extracted), Ignore(ExtractionFailed), Ignore(Reviewed));
        During(AwaitingReview, Ignore(Extracted), Ignore(ExtractionFailed), Ignore(Validated));

        SetCompletedWhenFinalized();
    }

    public State Extracting { get; private set; } = null!;
    public State Validating { get; private set; } = null!;
    public State AwaitingReview { get; private set; } = null!;

    public Event<DocumentReceived> Received { get; private set; } = null!;
    public Event<DocumentExtracted> Extracted { get; private set; } = null!;
    public Event<DocumentExtractionFailed> ExtractionFailed { get; private set; } = null!;
    public Event<DocumentValidated> Validated { get; private set; } = null!;
    public Event<DocumentReviewed> Reviewed { get; private set; } = null!;
}
