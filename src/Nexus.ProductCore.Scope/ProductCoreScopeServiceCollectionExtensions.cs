using Microsoft.Extensions.DependencyInjection;
using Nexus.ProductCore.Contracts;
using Nexus.ProductCore.Scope.Registration;

namespace Nexus.ProductCore.Scope;

public static class ProductCoreScopeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scope-kind registry as a process-lifetime singleton. Registration of
    /// consumer-defined kinds (Developer's Feature/Task/Subtask, etc.) happens once at host
    /// startup, immediately after this call - see M-06-1.2.
    /// </summary>
    public static IServiceCollection AddProductCoreScope(this IServiceCollection services)
    {
        services.AddSingleton<IScopeKindRegistry, ScopeKindRegistry>();

        return services;
    }
}
