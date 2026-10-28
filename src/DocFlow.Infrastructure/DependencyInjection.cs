using System.ClientModel;
using Azure.Storage.Blobs;
using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Domain;
using DocFlow.Infrastructure.Extraction;
using DocFlow.Infrastructure.Messaging;
using DocFlow.Infrastructure.Pdf;
using DocFlow.Infrastructure.Persistence;
using DocFlow.Infrastructure.Storage;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace DocFlow.Infrastructure;

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}

public static class DependencyInjection
{
    public static IServiceCollection AddDocFlowPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DocFlowDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DocFlow")));
        services.AddScoped<DocumentRepository>();
        services.AddScoped<IDocumentRepository>(sp => sp.GetRequiredService<DocumentRepository>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DocumentRepository>());
        return services;
    }

    public static IServiceCollection AddDocFlowStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BlobStorageOptions>(configuration.GetSection(BlobStorageOptions.Section));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            return new BlobContainerClient(options.ConnectionString, options.Container);
        });
        services.AddSingleton<AzureBlobDocumentStorage>();
        services.AddSingleton<IDocumentStorage>(sp => sp.GetRequiredService<AzureBlobDocumentStorage>());
        return services;
    }

    public static IServiceCollection AddDocFlowExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        var llm = configuration.GetSection(LlmOptions.Section).Get<LlmOptions>() ?? new LlmOptions();
        services.AddSingleton<IPdfTextReader, PdfPigTextReader>();
        services.AddSingleton<HeuristicInvoiceExtractor>();

        if (!llm.Enabled)
        {
            services.AddSingleton<IInvoiceExtractor>(sp => sp.GetRequiredService<HeuristicInvoiceExtractor>());
            return services;
        }

        services.AddSingleton(new ChatClient(
            llm.Model,
            new ApiKeyCredential(llm.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(llm.BaseUrl!),
                NetworkTimeout = TimeSpan.FromSeconds(llm.TimeoutSeconds),
            }));
        services.AddSingleton(llm);
        services.AddSingleton<OpenAIInvoiceExtractor>();
        services.AddSingleton<IInvoiceExtractor>(sp => new FallbackInvoiceExtractor(
            sp.GetRequiredService<OpenAIInvoiceExtractor>(),
            sp.GetRequiredService<HeuristicInvoiceExtractor>(),
            sp.GetRequiredService<ILogger<FallbackInvoiceExtractor>>()));
        return services;
    }

    public static IServiceCollection AddDocFlowOrchestration(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureBus = null)
    {
        services.AddScoped<IMessagePublisher, MassTransitPublisher>();
        services.AddScoped<StartExtractionActivity>();
        services.AddScoped<RecordExtractionActivity>();
        services.AddScoped<FailExtractionActivity>();
        services.AddScoped<CompleteValidationActivity>();

        var kafka = configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddDocFlowRetryPolicy();
            x.AddConfigureEndpointsCallback((context, _, endpoint) =>
                endpoint.UseEntityFrameworkOutbox<DocFlowDbContext>(context));

            x.AddEntityFrameworkOutbox<DocFlowDbContext>(outbox =>
            {
                outbox.UseSqlServer();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
                outbox.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.AddSagaStateMachine<DocumentStateMachine, DocumentState>()
                .EntityFrameworkRepository(repository =>
                {
                    repository.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    repository.ExistingDbContext<DocFlowDbContext>();
                    repository.UseSqlServer();
                });

            x.AddConsumer<KafkaAuditForwarder>().Endpoint(e => e.Name = Queues.KafkaAuditForwarder);

            x.AddRider(rider =>
            {
                rider.AddProducer<DocumentStatusChanged>(Topics.DocumentEvents);
                rider.UsingKafka((_, k) => k.Host(kafka));
            });

            configure?.Invoke(x);
            x.UsingDocFlowRabbitMq(configuration, configureBus);
        });

        return services;
    }

    public static void AddDocFlowRetryPolicy(this IBusRegistrationConfigurator x) =>
        x.AddConfigureEndpointsCallback((_, _, endpoint) =>
            endpoint.UseMessageRetry(retry =>
            {
                retry.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500));
                retry.Ignore<DomainException>();
                retry.Ignore<DocumentNotFoundException>();
            }));

    public static void UsingDocFlowRabbitMq(
        this IBusRegistrationConfigurator x,
        IConfiguration configuration,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureBus = null)
    {
        var rabbit = configuration.GetSection(RabbitMqOptions.Section).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        x.UsingRabbitMq((context, bus) =>
        {
            bus.Host(rabbit.Host, "/", host =>
            {
                host.Username(rabbit.Username);
                host.Password(rabbit.Password);
            });
            configureBus?.Invoke(context, bus);
            bus.ConfigureEndpoints(context);
        });
    }
}
