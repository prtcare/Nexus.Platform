using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Configuration;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mapping;
using NexusAI.Infrastructure.Dataverse.Repositories;
using NexusAI.Domain.Project;


namespace NexusAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IDataverseContext, InMemoryDataverseContext>();

        services.AddSingleton<IDataverseClient, DataverseClient>();

        services.Configure<DataverseOptions>(
            configuration.GetSection(DataverseOptions.SectionName));

        services.AddSingleton<
            IRepositoryMapper<Workspace, WorkspaceEntity>,
            WorkspaceMapper>();

        services.AddScoped<IWorkspaceRepository, WorkspaceDataverseRepository>();
        services.AddSingleton<
    IRepositoryMapper<Project, ProjectEntity>,
    ProjectMapper>();

        services.AddScoped<IProjectRepository, ProjectDataverseRepository>();



        return services;
    }
}