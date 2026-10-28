using DocFlow.Contracts;
using DocFlow.Infrastructure;
using DocFlow.Infrastructure.Messaging;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMassTransit(x =>
{
    x.AddDocFlowRetryPolicy();
    x.AddConsumer<ValidateDocumentConsumer>().Endpoint(e => e.Name = Queues.ValidateDocument);
    x.UsingDocFlowRabbitMq(builder.Configuration);
});

builder.Build().Run();
