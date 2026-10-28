using System.Text.Json.Serialization;
using DocFlow.Api;
using DocFlow.Application;
using DocFlow.Infrastructure;
using DocFlow.Infrastructure.Persistence;
using DocFlow.Infrastructure.Storage;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDocFlowApplication(builder.Configuration)
    .AddDocFlowPersistence(builder.Configuration)
    .AddDocFlowStorage(builder.Configuration)
    .AddDocFlowOrchestration(
        builder.Configuration,
        x => x.AddConsumer<LiveStatusConsumer>().ExcludeFromConfigureEndpoints(),
        (context, bus) => bus.ReceiveEndpoint(
            new TemporaryEndpointDefinition("live-status"),
            endpoint => endpoint.ConfigureConsumer<LiveStatusConsumer>(context)));

builder.Services.AddSingleton<DocumentEventBroadcaster>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DatabaseMigrator.MigrateWithRetryAsync(
        scope.ServiceProvider.GetRequiredService<DocFlowDbContext>(),
        logger,
        app.Lifetime.ApplicationStopping);
    await scope.ServiceProvider.GetRequiredService<AzureBlobDocumentStorage>()
        .EnsureContainerAsync(app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();
app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));
app.MapDocumentEndpoints();

app.Run();

public partial class Program;
