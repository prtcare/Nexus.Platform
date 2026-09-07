using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Platform.Contracts.Core;
using Nexus.Platform.Contracts.Models;
using Nexus.Platform.Core.Models;
using Nexus.Platform.Core.ProductCore;

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

        return services;
    }

    /// <summary>
    /// L01 CORE model/provider infrastructure registrations: model catalog, model
    /// gateway, and usage measurement. Batch 08/09 correction: this method's name
    /// ("AddNexusAi") and this comment previously called these registrations
    /// "L04 AI"/"AI-owned" -- LAYER_MODEL.md and DEPENDENCY_RULES.md are unambiguous
    /// that L04 AI is Nexus.Intelligence.* in a separate repository, and everything
    /// registered here (IModelCatalog, IModelGateway, IUsageMeter) is L01 CORE-owned
    /// provider/model infrastructure per LAYER_MODEL.md's own CORE "Owns" list
    /// ("usage metering, model gateway and routing") and CORE "Projects (TARGET)"
    /// list (which names Nexus.Platform.Providers.OpenAI/.Anthropic explicitly) --
    /// see architecture/NEXUS_V2_EXECUTION_BATCH_08_REPORT.md and
    /// _BATCH_09_REPORT.md. The method name itself is left unchanged in Batch 09 (a
    /// public-API rename was not in that batch's scope); renaming it to something
    /// like AddNexusCoreModelInfrastructure is recorded as a deferred, bounded
    /// Batch 10 candidate.
    /// </summary>
    public static IServiceCollection AddNexusAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModelCatalog, AggregatingModelCatalog>();
        services.AddSingleton<IModelGateway, RoutingModelGateway>();
        services.AddSingleton<IUsageMeter, InMemoryUsageMeter>();

        return services;
    }

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
    /// L03 Governance registrations. Currently empty: Governance's real ownership
    /// (per LAYER_MODEL.md) is registries -- product, technology, brand, compliance,
    /// licence, configuration -- none of which have a real implementation yet.
    /// (Future: IProductRegistry once a real implementation exists -- see
    /// architecture/NEXUS_V2_EXECUTION_BATCH_05_REPORT.md.) Audit logging, previously
    /// registered here, moved to AddNexusCore in Batch 07 -- it was never actually
    /// Governance-owned; see architecture/NEXUS_V2_EXECUTION_BATCH_07_REPORT.md.
    /// Kept as an explicit method (rather than removed) so AddNexusPlatform's
    /// composition shape stays stable as Governance registrations are added.
    /// </summary>
    public static IServiceCollection AddNexusGovernance(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    /// <summary>
    /// Backward-compatible facade composing the layer-level registration methods
    /// above. Existing callers of AddNexusPlatform see no behavior change: the same
    /// services are registered, with the same implementations and lifetimes.
    /// </summary>
    public static IServiceCollection AddNexusPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddNexusCore(configuration);
        services.AddNexusAi(configuration);
        services.AddNexusProductCore(configuration);
        services.AddNexusGovernance(configuration);

        return services;
    }
}
