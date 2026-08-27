using Microsoft.Extensions.DependencyInjection;

namespace Nexus.ProductCore.Contracts;

public static class ProductCoreContractsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a process-local <see cref="IScopeKindRegistry"/> singleton. Any consumer
    /// (Nexus.ProductCore's own Scope host, Nexus.Developer, a future machine domain, etc.)
    /// calls this once at startup, then registers its own scope kinds against the resolved
    /// instance. See <see cref="ScopeKindRegistry"/>'s remarks for why this is per-process,
    /// not shared across hosts, as of CHG-20260827-002.
    /// </summary>
    public static IServiceCollection AddScopeKindRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IScopeKindRegistry, ScopeKindRegistry>();

        return services;
    }
}
