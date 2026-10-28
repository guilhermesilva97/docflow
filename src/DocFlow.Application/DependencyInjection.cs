using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDocFlowApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<DocumentIngestion>();
        services.AddScoped<DocumentWorkflow>();
        services.AddScoped<DocumentReview>();
        return services;
    }
}
