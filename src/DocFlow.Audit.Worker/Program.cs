using Confluent.Kafka;
using DocFlow.Audit.Worker;
using DocFlow.Contracts;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var kafka = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Audit")));

builder.Services.AddMassTransit(x =>
{
    x.UsingInMemory();
    x.AddRider(rider =>
    {
        rider.AddConsumer<AuditConsumer>();
        rider.UsingKafka((context, k) =>
        {
            k.Host(kafka);
            k.TopicEndpoint<DocumentStatusChanged>(Topics.DocumentEvents, "docflow-audit", endpoint =>
            {
                endpoint.AutoOffsetReset = AutoOffsetReset.Earliest;
                endpoint.CreateIfMissing(topic => topic.NumPartitions = 1);
                endpoint.ConfigureConsumer<AuditConsumer>(context);
            });
        });
    });
});

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuditDbContext>>();
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
        }
        catch (SqlException error) when (attempt < 20)
        {
            logger.LogWarning("Banco de auditoria indisponível (tentativa {Attempt}): {Message}", attempt, error.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

host.Run();
