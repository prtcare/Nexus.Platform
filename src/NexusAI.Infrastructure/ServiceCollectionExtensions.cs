using Microsoft.Extensions.DependencyInjection;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Repositories.Workspace;
using NexusAI.Infrastructure.Dataverse;
using NexusAI.Infrastructure.Dataverse.Repositories;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Configuration;
using Microsoft.Extensions.Configuration;

namespace NexusAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
    {
        // Infrastructure services will be registered here.
        services.AddSingleton<IWorkspaceRepository, InMemoryWorkspaceRepository>();
        services.AddSingleton<IDataverseContext, InMemoryDataverseContext>();
        services.AddScoped<IWorkspaceRepository, WorkspaceDataverseRepository>();
        services.AddSingleton<IDataverseClient, DataverseClient>();
        services.Configure<DataverseOptions>(
    configuration.GetSection(DataverseOptions.SectionName));
        return services;
    }
}