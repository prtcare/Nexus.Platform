using Microsoft.Extensions.DependencyInjection;
using NexusAI.Application.Workspaces;
namespace NexusAI.Application.DependencyInjection;

using NexusAI.Application.Workspaces.Commands.CreateWorkspace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddWorkspaces();
        services.AddScoped<CreateWorkspaceHandler>();

        return services;
    }
}