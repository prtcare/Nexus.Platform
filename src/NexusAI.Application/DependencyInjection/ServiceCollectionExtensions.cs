using Microsoft.Extensions.DependencyInjection;
using NexusAI.Application.Workspaces;
namespace NexusAI.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddWorkspaces();

        return services;
    }
}