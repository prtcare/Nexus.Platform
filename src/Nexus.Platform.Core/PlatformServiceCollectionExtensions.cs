using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Contracts.Governance;
using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Core.Governance;
using Nexus.Platform.Core.Models;

namespace Nexus.Platform.Core;

public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddNexusPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModelCatalog, AggregatingModelCatalog>();
        services.AddSingleton<IModelGateway, RoutingModelGateway>();
        services.AddSingleton<IUsageMeter, InMemoryUsageMeter>();
        services.AddSingleton<IQuotaPolicy, PermissiveQuotaPolicy>();
        services.AddSingleton<IAuditLog, ConsoleAuditLog>();

        return services;
    }
}
