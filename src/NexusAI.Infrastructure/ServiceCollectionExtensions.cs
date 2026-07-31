using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAI.Application.Adr.Commands;
using NexusAI.Application.Artifact.Commands;
using NexusAI.Application.Session.Commands;
using NexusAI.Application.WorkItem;
using NexusAI.Domain.Adr;
using NexusAI.Domain.Artifact;
using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.Knowledge;
using NexusAI.Domain.Project;
using NexusAI.Domain.Session;
using NexusAI.Domain.WorkItem;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Configuration;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mappers;
using NexusAI.Infrastructure.Dataverse.Mapping;
using NexusAI.Infrastructure.Dataverse.Repositories;


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

        services.AddSingleton<
    IRepositoryMapper<Conversation, ConversationEntity>,
    ConversationMapper>();

        services.AddScoped<
            IConversationRepository,
            ConversationDataverseRepository>();

        services.AddSingleton<IRepositoryMapper<WorkItem, WorkItemEntity>, WorkItemMapper>();

        services.AddScoped<IWorkItemRepository, WorkItemDataverseRepository>();
        services.AddScoped<CreateWorkItemHandler>();
        services.AddSingleton<IRepositoryMapper<Session, SessionEntity>, SessionMapper>();
        services.AddScoped<CreateSessionHandler>();
        services.AddScoped<ISessionRepository, SessionDataverseRepository>();
        services.AddSingleton<
    IRepositoryMapper<Knowledge, KnowledgeEntity>,
    KnowledgeMapper>();

        services.AddScoped<
            IKnowledgeRepository,
            KnowledgeDataverseRepository>();
        services.AddSingleton<
    IRepositoryMapper<Branch, BranchEntity>,
    BranchMapper>();

        services.AddScoped<
            IBranchRepository,
            BranchDataverseRepository>();

        services.AddSingleton<
    IRepositoryMapper<Artifact, ArtifactEntity>,
    ArtifactMapper>();

        services.AddScoped<
            IArtifactRepository,
            ArtifactDataverseRepository>();
        services.AddScoped<CreateArtifactHandler>();
        services.AddSingleton<IRepositoryMapper<Adr, AdrEntity>, AdrMapper>();

        services.AddScoped<IAdrRepository, AdrDataverseRepository>();
        services.AddScoped<CreateAdrHandler>();


        return services;
    }
}