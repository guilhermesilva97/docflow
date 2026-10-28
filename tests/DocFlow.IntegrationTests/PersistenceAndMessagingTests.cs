using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Domain;
using DocFlow.Infrastructure;
using DocFlow.Infrastructure.Messaging;
using DocFlow.Infrastructure.Persistence;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocFlow.IntegrationTests;

public sealed class PersistenceAndMessagingTests : IAsyncLifetime
{
    private readonly string _database = $"DocFlowTest_{Guid.NewGuid():N}";
    private IHost? _host;

    public async Task InitializeAsync()
    {
        if (!IntegrationEnvironment.Enabled)
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DocFlow"] = IntegrationEnvironment.ConnectionString(_database),
                ["RabbitMq:Host"] = IntegrationEnvironment.RabbitHost,
            })
            .Build();

        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddDocFlowApplication(configuration)
            .AddDocFlowPersistence(configuration)
            .AddSingleton<IDocumentStorage, NullStorage>()
            .AddScoped<IMessagePublisher, MassTransitPublisher>()
            .AddSingleton<ValidatedProbe>()
            .AddMassTransit(x =>
            {
                x.AddEntityFrameworkOutbox<DocFlowDbContext>(outbox =>
                {
                    outbox.UseSqlServer();
                    outbox.UseBusOutbox(bus => bus.DisableDeliveryService());
                });
                x.AddConsumer<ValidateDocumentConsumer>()
                    .Endpoint(e =>
                    {
                        e.Name = $"validate-document-test-{Guid.NewGuid():N}";
                        e.Temporary = true;
                    });
                x.AddConsumer<ValidatedProbeConsumer>()
                    .Endpoint(e =>
                    {
                        e.Name = $"validated-probe-test-{Guid.NewGuid():N}";
                        e.Temporary = true;
                    });
                x.UsingDocFlowRabbitMq(configuration);
            });
        _host = builder.Build();

        using (var scope = _host.Services.CreateScope())
        {
            await DatabaseMigrator.MigrateWithRetryAsync(
                scope.ServiceProvider.GetRequiredService<DocFlowDbContext>(),
                NullLogger.Instance,
                CancellationToken.None);
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is null)
        {
            return;
        }

        using (var scope = _host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DocFlowDbContext>().Database.EnsureDeletedAsync();
        }

        await _host.StopAsync();
        _host.Dispose();
    }

    [IntegrationFact]
    public async Task Migrations_create_schema_and_aggregate_round_trips_with_json_columns()
    {
        var id = Guid.NewGuid();
        using (var scope = _host!.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var document = Document.Receive(id, "nf.pdf", "blob", DateTimeOffset.UtcNow);
            document.StartExtraction(DateTimeOffset.UtcNow);
            document.RecordExtraction(
                new ExtractionResult(IntegrationEnvironment.Invoice("000777"), 0.93, "llm"),
                DateTimeOffset.UtcNow);
            repository.Add(document);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = _host.Services.CreateScope())
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>()
                .GetAsync(id, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(DocumentStatus.Extracted, loaded.Status);
            Assert.Equal(3, loaded.History.Count);
            Assert.Equal(200m, loaded.Invoice!.Items.Single().Total);
            Assert.Equal("11222333000181|777", loaded.DuplicateKey);
        }
    }

    [IntegrationFact]
    public async Task Ingestion_writes_document_and_outbox_messages_in_the_same_transaction()
    {
        Guid id;
        using (var scope = _host!.Services.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<DocumentIngestion>()
                .ReceiveAsync(new MemoryStream([1, 2, 3]), "nota.pdf", CancellationToken.None);
        }

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocFlowDbContext>();
            Assert.True(await db.Documents.AnyAsync(d => d.Id == id));
            var outbox = await db.Set<OutboxMessage>().Select(m => m.MessageType).ToListAsync();
            Assert.Contains(outbox, t => t.Contains(nameof(DocumentReceived)));
            Assert.Contains(outbox, t => t.Contains(nameof(DocumentStatusChanged)));
        }
    }

    [IntegrationFact]
    public async Task Duplicate_lookup_only_matches_approved_documents()
    {
        using var scope = _host!.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var approved = Document.Receive(Guid.NewGuid(), "a.pdf", "a", DateTimeOffset.UtcNow);
        approved.StartExtraction(DateTimeOffset.UtcNow);
        approved.RecordExtraction(new ExtractionResult(IntegrationEnvironment.Invoice("000888"), 0.95, "llm"), DateTimeOffset.UtcNow);
        approved.StartValidation(DateTimeOffset.UtcNow);
        approved.CompleteValidation([], 0.8, false, DateTimeOffset.UtcNow);
        repository.Add(approved);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        Assert.True(await repository.ExistsApprovedWithKeyAsync("11222333000181|888", Guid.NewGuid(), CancellationToken.None));
        Assert.False(await repository.ExistsApprovedWithKeyAsync("11222333000181|888", approved.Id, CancellationToken.None));
        Assert.False(await repository.ExistsApprovedWithKeyAsync("11222333000181|999", Guid.NewGuid(), CancellationToken.None));
    }

    [IntegrationFact]
    public async Task Validation_round_trip_through_a_real_rabbitmq_broker()
    {
        var probe = _host!.Services.GetRequiredService<ValidatedProbe>();
        var id = Guid.NewGuid();
        var invoice = IntegrationEnvironment.Invoice("000999") with { Total = 999m };

        await _host.Services.GetRequiredService<IBus>()
            .Publish(new ValidateDocument(id, invoice.ToPayload()));

        var validated = await probe.WaitForAsync(id, TimeSpan.FromSeconds(30));
        Assert.Contains(validated.Issues, i => i.Code == "total_mismatch");
    }

    private sealed class NullStorage : IDocumentStorage
    {
        public Task SaveAsync(string blobName, Stream content, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Stream> OpenAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());
    }
}

public sealed class ValidatedProbe
{
    private readonly Dictionary<Guid, TaskCompletionSource<DocumentValidated>> _waiters = [];

    public Task<DocumentValidated> WaitForAsync(Guid id, TimeSpan timeout)
    {
        lock (_waiters)
        {
            return Waiter(id).Task.WaitAsync(timeout);
        }
    }

    public void Record(DocumentValidated message)
    {
        lock (_waiters)
        {
            Waiter(message.DocumentId).TrySetResult(message);
        }
    }

    private TaskCompletionSource<DocumentValidated> Waiter(Guid id)
    {
        if (!_waiters.TryGetValue(id, out var waiter))
        {
            waiter = new TaskCompletionSource<DocumentValidated>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[id] = waiter;
        }

        return waiter;
    }
}

public sealed class ValidatedProbeConsumer(ValidatedProbe probe) : IConsumer<DocumentValidated>
{
    public Task Consume(ConsumeContext<DocumentValidated> context)
    {
        probe.Record(context.Message);
        return Task.CompletedTask;
    }
}
