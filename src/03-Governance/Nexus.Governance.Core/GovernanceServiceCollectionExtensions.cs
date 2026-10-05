using Microsoft.Extensions.DependencyInjection;
using Nexus.Governance.Contracts;

namespace Nexus.Governance.Core;

/// <summary>
/// L03 GOVERNANCE service-collection registrations.
/// </summary>
public static class GovernanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GOVERNANCE product registry (and its in-memory store) as a
    /// process-lifetime singleton. This is the real Governance registration -- compare the
    /// intentional no-op AddNexusGovernance in Nexus.Platform.Core, which CORE keeps only so
    /// its composition shape stays stable and which may not reference this assembly
    /// (DEPENDENCY_RULES.md row 01 CORE has no "may reference" cell for column 03
    /// GOVERNANCE). A host composes both at the composition root. Not yet wired into any
    /// host: the only host that ever composed Platform was Nexus.Platform.SmokeHost, retired
    /// in the W10.0A canonical integration once the provider it exercised left Platform.
    /// </summary>
    public static IServiceCollection AddGovernance(this IServiceCollection services)
    {
        services.AddSingleton<IProductRegistry>(_ => new ProductRegistryService(new InMemoryProductStore()));

        // W8B. The deterministic governance evaluator is the Contract Plane's only implementation.
        // Registered as the INTERFACE so a consuming host programs against IGovernanceEvaluator and
        // never against this assembly's concrete type -- Forge depends on Contracts, and resolves
        // the implementation here at its composition root rather than referencing it directly.
        services.AddSingleton<IGovernanceEvaluator>(_ => new DeterministicGovernanceEvaluator());

        return services;
    }
}
