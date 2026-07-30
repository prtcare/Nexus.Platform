using Microsoft.Extensions.DependencyInjection;

namespace NexusAI.Application.Workspaces;

public static class WorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaces(
        this IServiceCollection services)
    {
        return services;
    }
}