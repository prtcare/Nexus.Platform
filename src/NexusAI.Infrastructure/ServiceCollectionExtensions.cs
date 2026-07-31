using Microsoft.Extensions.DependencyInjection;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Repositories.Workspace;

namespace NexusAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        // Infrastructure services will be registered here.
        services.AddSingleton<IWorkspaceRepository, InMemoryWorkspaceRepository>();
        return services;
    }
}