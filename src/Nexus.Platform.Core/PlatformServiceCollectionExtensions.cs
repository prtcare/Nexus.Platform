using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Platform.Contracts.Core;
using Nexus.Platform.Contracts.Secrets;
using Nexus.Platform.Core.ProductCore;
using Nexus.Platform.Core.Secrets;

namespace Nexus.Platform.Core;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// L01 CORE registrations: cross-layer neutral primitives. Audit logging is a
    /// CORE-owned abstraction, not a Governance one -- LAYER_MODEL.md's L01 CORE
    /// "Owns" list names "audit" explicitly, its "Minimum before the gate" names
    /// "durable IAuditLog replacing ConsoleAuditLog" as a CORE deliverable, and
    /// DEPENDENCY_RULES.md's Rule 2 describes audit/logging/events as reached
    /// "through a CORE-owned abstraction". IAuditLog/AuditEntry/ConsoleAuditLog were
    /// misclassified under a Governance folder/namespace; this batch corrects that --
    /// see architecture/NEXUS_V2_EXECUTION_BATCH_07_REPORT.md.
    /// </summary>
    public static IServiceCollection AddNexusCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAuditLog, ConsoleAuditLog>();

        // W5E / D-14: the NEUTRAL secret-access boundary is registered here, in CORE,
        // because that is the one layer every consumer may reference.
        //
        // TryAdd (not Add) so a host can bind its own ISecretResolver -- the documented
        // IQuotaPolicy/PermissiveQuotaPolicy composition-root pattern: the port lives in
        // neutral Contracts, the implementation is chosen by the host. A host that
        // registers a resolver AFTER this call also wins, because the last registration
        // is the one GetRequiredService returns.
        //
        // The default implementation resolves against the environment and touches no
        // credential store, so CORE never enters D-14 custody.
        services.TryAddSingleton<ISecretResolver, EnvironmentSecretResolver>();

        return services;
    }

    // AddNexusAi was REMOVED here by W5G (2026-09-12). It registered
    // IModelCatalog/AggregatingModelCatalog, IModelGateway/RoutingModelGateway and
    // IUsageMeter/InMemoryUsageMeter.
    //
    // OWNER DECISION F-01 relocates the whole model-catalog domain to the AI Head
    // (D:\NEXUS\AI\Intelligence): the contracts IModelCatalog/IModelCatalogSource/
    // INamedModelGateway and the implementations AggregatingModelCatalog/
    // RoutingModelGateway/InMemoryUsageMeter are AI-domain semantics, and Platform may
    // reach AI only through provider-neutral PL-04 Contract Plane capability contracts.
    // The types, this method and its call site all leave Nexus.Platform in the same
    // change; registering them from here would have re-created the duplicate type
    // identity F-01 exists to remove. The AI Head now registers its own model domain
    // through NexuIntelligenceServiceCollectionExtensions.AddNexusIntelligence.
    //
    // Consequence for hosts: AddNexusPlatform no longer resolves IModelCatalog,
    // IModelGateway or IUsageMeter. A host that needs the model domain composes the AI
    // Head at its own composition root, exactly as it already composes GOVERNANCE.

    /// <summary>
    /// L06 Product Core registrations: quota/entitlement policy. AI consumes this
    /// decision (see OpenAIModelGateway) but does not own it -- see the governing
    /// principle recorded in BATCH_05_REPORT.md/BATCH_06_REPORT.md: "the layer that
    /// measures an event does not automatically own the entitlement or governance
    /// policy applied to that event." The IQuotaPolicy/QuotaVerdict CONTRACT lives in
    /// the neutral Nexus.Platform.Contracts.Core namespace (DEPENDENCY_RULES.md forbids
    /// 04 AI <-> 06 PRODUCT CORE as an architectural relationship in either direction,
    /// not merely as an assembly reference; LAYER_MODEL.md's L01 CORE "Owns" list names
    /// "policy evaluation" as a CORE-owned foundation responsibility today). The
    /// IMPLEMENTATION registered here remains Product-Core-owned -- see
    /// architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md, "Architecture Correction".
    /// </summary>
    public static IServiceCollection AddNexusProductCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IQuotaPolicy, PermissiveQuotaPolicy>();

        return services;
    }

    /// <summary>
    /// L03 GOVERNANCE registrations. This method is an intentional NO-OP: Governance's real
    /// registrations now live in the L03 GOVERNANCE leaf assembly Nexus.Governance.Core and
    /// are registered through its own GovernanceServiceCollectionExtensions.AddGovernance
    /// (M-03-1.1/M-03-1.2; see architecture/SP1_P01_GOVERNANCE_PRODUCT_IDENTITY_REPORT.md).
    /// CORE cannot call that method from here: DEPENDENCY_RULES.md's matrix gives row 01 CORE
    /// no "may reference" cell for column 03 GOVERNANCE, so Nexus.Platform.Core must not take
    /// a project/type reference on Nexus.Governance.*. A host composes CORE and GOVERNANCE at
    /// the composition root -- AddNexusPlatform(...) for CORE, then AddGovernance(...) for
    /// GOVERNANCE. Audit logging, previously registered here, moved to AddNexusCore in Batch 07
    /// -- it was never actually Governance-owned (architecture/NEXUS_V2_EXECUTION_BATCH_07_REPORT.md).
    /// Kept as an explicit method (rather than removed) so AddNexusPlatform's composition
    /// shape stays stable as Governance registrations are added.
    /// </summary>
    public static IServiceCollection AddNexusGovernance(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    /// <summary>
    /// Facade composing the layer-level registration methods above.
    ///
    /// W5G / F-01 BEHAVIOR CHANGE, deliberate and the whole point of the change: this
    /// method no longer registers the model domain. IModelCatalog, IModelGateway and
    /// IUsageMeter used to be registered here through AddNexusAi; they are AI-Head
    /// types as of F-01 and are registered by the AI Head. Callers of
    /// AddNexusPlatform(...) that need the model domain must now compose the AI Head
    /// alongside it -- AddNexusPlatform(...) for CORE, then the AI Head's registration.
    /// Everything else registered here (IAuditLog, ISecretResolver, IQuotaPolicy) is
    /// unchanged, with the same implementations and lifetimes.
    /// </summary>
    public static IServiceCollection AddNexusPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddNexusCore(configuration);
        services.AddNexusProductCore(configuration);
        services.AddNexusGovernance(configuration);

        return services;
    }
}
