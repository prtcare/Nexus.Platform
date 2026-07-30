using Microsoft.Extensions.DependencyInjection;

namespace NexusAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        // Infrastructure services will be registered here.

        return services;
    }
}