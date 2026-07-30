using Microsoft.Extensions.DependencyInjection;
using NexusAI.Core.Abstractions;
using NexusAI.Infrastructure.Modules;
using NexusAI.Infrastructure.Services;

namespace NexusAI.Host.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNexusAI(this IServiceCollection services)
    {
        services.AddInfrastructureModules();

        return services;
    }
}