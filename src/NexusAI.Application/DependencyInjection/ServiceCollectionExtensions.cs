using Microsoft.Extensions.DependencyInjection;
using NexusAI.Application.Workspaces;
namespace NexusAI.Application.DependencyInjection;

using NexusAI.Application.Conversations.Commands.CreateConversation;
using NexusAI.Application.Knowledge.Commands;
using NexusAI.Application.Projects.Commands.CreateProject;
using NexusAI.Application.Workspaces.Commands.CreateWorkspace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddWorkspaces();
        services.AddScoped<CreateWorkspaceHandler>();
        services.AddScoped<CreateProjectHandler>();
        services.AddScoped<CreateConversationHandler>();
        services.AddScoped<CreateKnowledgeHandler>();

        return services;
    }
}