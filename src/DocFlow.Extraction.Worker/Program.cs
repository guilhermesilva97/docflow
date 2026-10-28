using DocFlow.Contracts;
using DocFlow.Infrastructure;
using DocFlow.Infrastructure.Messaging;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);
var concurrency = builder.Configuration.GetValue("Extraction:ConcurrentMessageLimit", 4);

builder.Services
    .AddDocFlowStorage(builder.Configuration)
    .AddDocFlowExtraction(builder.Configuration)
    .AddMassTransit(x =>
    {
        x.AddDocFlowRetryPolicy();
        x.AddConsumer<ExtractDocumentConsumer>().Endpoint(e =>
        {
            e.Name = Queues.ExtractDocument;
            e.ConcurrentMessageLimit = concurrency;
        });
        x.UsingDocFlowRabbitMq(builder.Configuration);
    });

builder.Build().Run();
