using Microsoft.Extensions.DependencyInjection;

namespace NexusAI.Infrastructure.Modules;

public static class ModuleExtensions
{
    public static IServiceCollection AddInfrastructureModules(this IServiceCollection services)
    {
        new CoreModule().Register(services);

        return services;
    }
}