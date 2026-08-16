using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using NexusAI.Application.Adr.Commands;
using NexusAI.Application.Artifact.Commands;
using NexusAI.Application.Artifact.Commands.UpdateArtifact;
using NexusAI.Application.Artifact.Queries.GetArtifact;
using NexusAI.Application.Artifact.Queries.ListArtifacts;
using NexusAI.Application.Branch.Commands;
using NexusAI.Application.Branch.Commands.UpdateBranch;
using NexusAI.Application.Branch.Queries.GetBranch;
using NexusAI.Application.Branch.Queries.ListBranches;
using NexusAI.Application.Chat;
using NexusAI.Application.Chat.Commands.SendChat;
using NexusAI.Application.Chat.Prompting;
using NexusAI.Application.ConversationMessages.Queries.GetConversationMessages;
using NexusAI.Application.Conversations.Commands.UpdateConversation;
using NexusAI.Application.Conversations.Queries.GetConversation;
using NexusAI.Application.Conversations.Queries.ListConversations;
using NexusAI.Application.Execution;
using NexusAI.Application.Execution.Commands;
using NexusAI.Application.Knowledge.Commands;
using NexusAI.Application.Knowledge.Queries.GetKnowledge;
using NexusAI.Application.Knowledge.Queries.ListKnowledge;
using NexusAI.Application.Knowledge.Services;
using NexusAI.Application.Planning;
using NexusAI.Application.Planning.Commands;
using NexusAI.Application.Projects.Commands.CreateProject;
using NexusAI.Application.Projects.Commands.UpdateProject;
using NexusAI.Application.Projects.Queries.GetProject;
using NexusAI.Application.Projects.Queries.ListProjects;
using NexusAI.Application.Providers;
using NexusAI.Application.Session.Commands;
using NexusAI.Application.Session.Commands.UpdateSession;
using NexusAI.Application.Session.Queries.GetSession;
using NexusAI.Application.Session.Queries.ListSessions;
using NexusAI.Application.Snapshot.Commands;
using NexusAI.Application.Snapshot.Commands.UpdateSnapshot;
using NexusAI.Application.Snapshot.Queries.GetSnapshot;
using NexusAI.Application.Snapshot.Queries.ListSnapshots;
using NexusAI.Application.WorkItem;
using NexusAI.Application.Workspaces.Commands.CreateWorkspace;
using NexusAI.Application.Workspaces.Commands.UpdateWorkspace;
using NexusAI.Application.Workspaces.Queries.GetWorkspace;
using NexusAI.Application.Workspaces.Queries.ListWorkspaces;
using NexusAI.Core.Abstractions;
using NexusAI.Core.Agents;
using NexusAI.Domain.Adr;
using NexusAI.Domain.Artifact;
using NexusAI.Domain.Branch;
using NexusAI.Domain.Conversation;
using NexusAI.Domain.ConversationMessage;
using NexusAI.Domain.Knowledge;
using NexusAI.Domain.Memory;
using NexusAI.Domain.Project;
using NexusAI.Domain.Session;
using NexusAI.Domain.Snapshot;
using NexusAI.Domain.WorkItem;
using NexusAI.Domain.Workspace;
using NexusAI.Infrastructure.Dataverse;
using NexusAI.Infrastructure.Dataverse.Clients;
using NexusAI.Infrastructure.Dataverse.Common;
using NexusAI.Infrastructure.Dataverse.Configuration;
using NexusAI.Infrastructure.Dataverse.Entities;
using NexusAI.Infrastructure.Dataverse.Mapping;
using NexusAI.Infrastructure.Dataverse.Repositories;
using NexusAI.Infrastructure.OpenAI;
using NexusAI.Infrastructure.Services;

namespace NexusAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ============================================================
        // ///CONFIGURATION
        // ============================================================

        services.Configure<OpenAIOptions>(
            configuration.GetSection(
                OpenAIOptions.SectionName));

        services.Configure<DataverseOptions>(
            configuration.GetSection(
                DataverseOptions.SectionName));

        // ============================================================
        // ///DATAVERSE CLIENT
        // ============================================================

        services.AddSingleton<ServiceClient>(
            serviceProvider =>
            {
                var options =
                    serviceProvider
                        .GetRequiredService<IOptions<DataverseOptions>>()
                        .Value;

                if (string.IsNullOrWhiteSpace(options.Url))
                {
                    throw new InvalidOperationException(
                        "Dataverse:Url is not configured.");
                }

                if (string.IsNullOrWhiteSpace(options.ClientId))
                {
                    throw new InvalidOperationException(
                        "Dataverse:ClientId is not configured.");
                }

                if (string.IsNullOrWhiteSpace(options.ClientSecret))
                {
                    throw new InvalidOperationException(
                        "Dataverse:ClientSecret is not configured.");
                }

                var connectionString =
                    $"AuthType=ClientSecret;" +
                    $"Url={options.Url};" +
                    $"ClientId={options.ClientId};" +
                    $"ClientSecret={options.ClientSecret};";

                var client = new ServiceClient(
                    connectionString);

                if (!client.IsReady)
                {
                    throw new InvalidOperationException(
                        "Unable to connect to Dataverse. " +
                        "Verify the Dataverse URL, TenantId, ClientId, and ClientSecret.");
                }

                return client;
            });

        services.AddSingleton<
            IDataverseContext,
            DataverseContext>();

        services.AddSingleton<
            IDataverseClient,
            DataverseClient>();

        // ============================================================
        // ///WORKSPACE PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Workspace, WorkspaceEntity>,
            WorkspaceMapper>();

        services.AddScoped<
            IWorkspaceRepository,
            WorkspaceDataverseRepository>();

        // ============================================================
        // ///PROJECT PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Project, ProjectEntity>,
            ProjectMapper>();

        services.AddScoped<
            IProjectRepository,
            ProjectDataverseRepository>();

        // ============================================================
        // ///WORK ITEM PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<WorkItem, WorkItemEntity>,
            WorkItemMapper>();

        services.AddScoped<
            IWorkItemRepository,
            WorkItemDataverseRepository>();

        // ============================================================
        // ///CONVERSATION PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Conversation, ConversationEntity>,
            ConversationMapper>();

        services.AddScoped<
            IConversationRepository,
            ConversationDataverseRepository>();

        // ============================================================
        // ///CONVERSATION MESSAGE PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<
                ConversationMessage,
                ConversationMessageEntity>,
            ConversationMessageMapper>();

        services.AddScoped<
            IConversationMessageRepository,
            ConversationMessageDataverseRepository>();

        // ============================================================
        // ///KNOWLEDGE PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Knowledge, KnowledgeEntity>,
            KnowledgeMapper>();

        services.AddScoped<
            IKnowledgeRepository,
            KnowledgeDataverseRepository>();

        // ============================================================
        // ///MEMORY PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Memory, MemoryEntity>,
            MemoryMapper>();

        services.AddScoped<
            IMemoryRepository,
            MemoryDataverseRepository>();

        // ============================================================
        // ///BRANCH PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Branch, BranchEntity>,
            BranchMapper>();

        services.AddScoped<
            IBranchRepository,
            BranchDataverseRepository>();

        // ============================================================
        // ///ARTIFACT PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Artifact, ArtifactEntity>,
            ArtifactMapper>();

        services.AddScoped<
            IArtifactRepository,
            ArtifactDataverseRepository>();

        // ============================================================
        // ///ADR PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Adr, AdrEntity>,
            AdrMapper>();

        services.AddScoped<
            IAdrRepository,
            AdrDataverseRepository>();

        // ============================================================
        // ///SESSION PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Session, SessionEntity>,
            SessionMapper>();

        services.AddScoped<
            ISessionRepository,
            SessionDataverseRepository>();

        // ============================================================
        // ///SNAPSHOT PERSISTENCE
        // ============================================================

        services.AddSingleton<
            IRepositoryMapper<Snapshot, SnapshotEntity>,
            SnapshotMapper>();

        services.AddScoped<
            ISnapshotRepository,
            SnapshotDataverseRepository>();

        // ============================================================
        // ///WORKSPACE APPLICATION
        // ============================================================

        services.AddScoped<CreateWorkspaceHandler>();
        services.AddScoped<GetWorkspaceHandler>();
        services.AddScoped<ListWorkspacesHandler>();
        services.AddScoped<UpdateWorkspaceHandler>();

        // ============================================================
        // ///PROJECT APPLICATION
        // ============================================================

        services.AddScoped<CreateProjectHandler>();
        services.AddScoped<GetProjectHandler>();
        services.AddScoped<ListProjectsHandler>();
        services.AddScoped<UpdateProjectHandler>();

        // ============================================================
        // ///WORK ITEM APPLICATION
        // ============================================================

        services.AddScoped<CreateWorkItemHandler>();
        services.AddScoped<GetWorkItemHandler>();
        services.AddScoped<ListWorkItemsHandler>();
        services.AddScoped<UpdateWorkItemHandler>();

        // ============================================================
        // ///CONVERSATION APPLICATION
        // ============================================================

        services.AddScoped<CreateConversationHandler>();
        services.AddScoped<GetConversationHandler>();
        services.AddScoped<ListConversationsHandler>();
        services.AddScoped<UpdateConversationHandler>();

        // ============================================================
        // ///CONVERSATION MESSAGE APPLICATION
        // ============================================================

        services.AddScoped<SendChatHandler>();

        services.AddScoped<GetConversationMessagesHandler>();

        // ============================================================
        // ///KNOWLEDGE APPLICATION
        // ============================================================

        services.AddScoped<CreateKnowledgeHandler>();
        services.AddScoped<GetKnowledgeHandler>();
        services.AddScoped<ListKnowledgeHandler>();

        // ============================================================
        // ///BRANCH
        // ============================================================

        services.AddScoped<CreateBranchHandler>();
        services.AddScoped<GetBranchHandler>();
        services.AddScoped<ListBranchesHandler>();
        services.AddScoped<UpdateBranchHandler>();

        // ============================================================
        // ///ARTIFACT APPLICATION
        // ============================================================

        services.AddScoped<CreateArtifactHandler>();
        services.AddScoped<GetArtifactHandler>();
        services.AddScoped<ListArtifactsHandler>();
        services.AddScoped<UpdateArtifactHandler>();

        // ============================================================
        // ///ADR APPLICATION
        // ============================================================

        services.AddScoped<CreateAdrHandler>();

        // ============================================================
        // ///SESSION APPLICATION
        // ============================================================

        services.AddScoped<CreateSessionHandler>();
        services.AddScoped<GetSessionHandler>();
        services.AddScoped<ListSessionsHandler>();
        services.AddScoped<UpdateSessionHandler>();

        // ============================================================
        // ///SNAPSHOT APPLICATION
        // ============================================================

        services.AddScoped<CreateSnapshotHandler>();
        services.AddScoped<GetSnapshotHandler>();
        services.AddScoped<ListSnapshotsHandler>();
        services.AddScoped<UpdateSnapshotHandler>();

        // ============================================================
        // ///KNOWLEDGE SERVICES
        // ============================================================

        services.AddScoped<IKnowledgeContextProvider, KnowledgeContextProvider>();

        services.AddScoped<IPromptBuilder, PromptBuilder>();

        services.AddScoped<
            IKnowledgeRetrievalService,
            KnowledgeRetrievalService>();

        services.AddScoped<
            IKnowledgeRanker,
            KeywordKnowledgeRanker>();

        // ============================================================
        // ///CHAT
        // ============================================================

        services.AddScoped<SendChatHandler>();

        services.AddScoped<IChatService, ChatService>();

        services.AddScoped<
            IConversationContextProvider,
            ConversationContextProvider>();

        // ============================================================
        // ///PLANNING
        // ============================================================

        services.AddScoped<IPlanner, Planner>();

        services.AddScoped<CreatePlanHandler>();

        // ============================================================
        // ///EXECUTION
        // ============================================================

        services.AddScoped<
            IExecutionEngine,
            ExecutionEngine>();

        services.AddScoped<ExecutePlanHandler>();

        // ============================================================
        // ///AGENTS
        // ============================================================

        services.AddScoped<
            IAgentRegistry,
            AgentRegistry>();

        services.AddScoped<
            IAgentDispatcher,
            AgentDispatcher>();

        // ============================================================
        // ///PROVIDERS
        // ============================================================

        services.AddSingleton<
            ILLMProvider,
            OpenAIProvider>();

        // ============================================================
        // ///CORE SERVICES
        // ============================================================

        services.AddSingleton<
            IClock,
            SystemClock>();

        return services;
    }
}