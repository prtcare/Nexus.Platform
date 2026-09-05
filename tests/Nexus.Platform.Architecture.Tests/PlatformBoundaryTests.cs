using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Nexus.Platform.Architecture.Tests;

public sealed class PlatformBoundaryTests
{
    private static readonly Assembly[] PlatformAssemblies =
    [
        typeof(Nexus.Platform.Contracts.Models.ModelDescriptor).Assembly,
        typeof(Nexus.Platform.Core.PlatformServiceCollectionExtensions).Assembly,
        typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway).Assembly
    ];

    private static readonly string[] ForbiddenProductTypeNames =
    [
        "Workspace", "Project", "Conversation", "ConversationMessage", "Knowledge",
        "WorkItem", "Artifact", "Branch", "Snapshot", "Session", "Adr"
    ];

    [Fact]
    public void Platform_MustNotReference_IntelligenceOrProducts()
    {
        foreach (var assembly in PlatformAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Nexus.Intelligence", "Nexus.Products")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assembly.GetName().Name} has a forbidden dependency: " +
                string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Platform_MustNotContain_ProductTypeNames()
    {
        var offenders = PlatformAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => ForbiddenProductTypeNames.Contains(t.Name))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, $"Forbidden product type names found: {string.Join(", ", offenders)}");
    }

    // Batch 06: lock in the Batch 05/06 ownership decisions so a future accidental
    // move (e.g. back into a Governance folder/namespace) is caught by the build.
    // See architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md.
    [Fact]
    public void UsageMeter_IsOwnedByAiLayer()
    {
        Assert.Equal("Nexus.Platform.Contracts.Models", typeof(Nexus.Platform.Contracts.Models.IUsageMeter).Namespace);
        Assert.Equal("Nexus.Platform.Contracts.Models", typeof(Nexus.Platform.Contracts.Models.UsageRecord).Namespace);
        Assert.Equal("Nexus.Platform.Core.Models", typeof(Nexus.Platform.Core.Models.InMemoryUsageMeter).Namespace);
    }

    // Corrected across two architecture-correction reviews requested before Windows
    // verification (see architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md,
    // "Architecture Correction" and its follow-up). The original Batch 06 placement
    // put IQuotaPolicy/QuotaVerdict in Nexus.Platform.Contracts.ProductCore, which
    // OpenAIModelGateway (L04 AI) imported directly -- a real L04 -> L06 architectural
    // dependency per DEPENDENCY_RULES.md's matrix, regardless of the absence of a
    // ProjectReference. The first correction relocated the contract to the neutral
    // Nexus.Platform.Contracts.Core namespace, but IQuotaPolicy still depended on
    // InvocationIdentity, which at that point lived in the AI-owned
    // Nexus.Platform.Contracts.Models namespace -- reintroducing the same class of
    // problem one level down (Core -> AI, and transitively Product Core -> AI via
    // PermissiveQuotaPolicy). InvocationIdentity has since been reclassified as a
    // CORE-owned identity primitive (see InvocationIdentity.cs's own comment: "the
    // metering key -- and deliberately the ONLY identity Platform ever sees... must
    // never be able to express a product's internal structure" -- the same shape and
    // intent as the already-CORE-classified Identity/ResolvedIdentity) and moved to
    // Nexus.Platform.Contracts.Core alongside IQuotaPolicy/QuotaVerdict. IQuotaPolicy
    // now depends only on other types in its own namespace. The entitlement POLICY
    // IMPLEMENTATION remains physically and conceptually Product-Core-owned.
    [Fact]
    public void QuotaPolicy_ContractIsNeutralCoreBoundary_ImplementationIsProductCoreOwned()
    {
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.IQuotaPolicy).Namespace);
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.QuotaVerdict).Namespace);
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.InvocationIdentity).Namespace);
        Assert.Equal("Nexus.Platform.Core.ProductCore", typeof(Nexus.Platform.Core.ProductCore.PermissiveQuotaPolicy).Namespace);
    }

    // Corrected in Batch 07 (see architecture/NEXUS_V2_EXECUTION_BATCH_07_REPORT.md).
    // IAuditLog/AuditEntry/ConsoleAuditLog were originally filed under a "Governance"
    // folder/namespace, but LAYER_MODEL.md's L01 CORE "Owns" list names "audit"
    // explicitly, its "Minimum before the gate" names "durable IAuditLog replacing
    // ConsoleAuditLog" as a CORE deliverable, and DEPENDENCY_RULES.md's Rule 2
    // describes audit/logging/events as reached "through a CORE-owned abstraction" --
    // none of which is true of anything actually in L03 GOVERNANCE's "Owns" list
    // (product/technology/brand/compliance/licence/configuration registries). Both
    // the contract and the concrete implementation are CORE-owned; this test replaces
    // the old (incorrect) AuditLog_RemainsOwnedByGovernanceLayer.
    [Fact]
    public void AuditLog_IsOwnedByCoreLayer()
    {
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.IAuditLog).Namespace);
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.AuditEntry).Namespace);
        Assert.Equal("Nexus.Platform.Core", typeof(Nexus.Platform.Core.ConsoleAuditLog).Namespace);
    }

    // Replaces the Batch 06 test of the same intent, which only asserted that the AI
    // provider assembly carried no ProjectReference named "Nexus.ProductCore.Contracts"
    // / "Nexus.ProductCore.Scope" -- TARGET assembly names that do not exist yet in
    // this pre-split repository, so that assertion passed trivially and proved
    // nothing about the actual architectural relationship (see
    // architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md, "Architecture Correction").
    // This uses NetArchTest's IL-level type-dependency analysis -- the same mechanism
    // Platform_MustNotReference_IntelligenceOrProducts already relies on -- to assert
    // the actual forbidden relationship: no type in the AI provider assembly may have
    // a compile-time dependency on the Product-Core-owned implementation namespace,
    // regardless of which physical assembly that namespace lives in today.
    [Fact]
    public void AiProviderAssembly_MustNotHaveTypeDependencyOn_ProductCoreOwnedNamespaces()
    {
        var openAiAssembly = typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway).Assembly;

        var result = Types.InAssembly(openAiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Nexus.Platform.Core.ProductCore", "Nexus.Platform.Contracts.ProductCore", "Nexus.ProductCore")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "AI provider has a forbidden type-level dependency on Product-Core-owned code: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    // Added in the InvocationIdentity follow-up correction: the neutral CORE boundary
    // only stays neutral if nothing inside it reaches back up into a layer that
    // depends on it. If a future edit reintroduced an AI-owned or Product-Core-owned
    // type dependency into Nexus.Platform.Contracts.Core, this fails immediately
    // rather than waiting for a human to notice during the next physical move.
    [Fact]
    public void ContractsCoreNamespace_MustNotDependOn_AiOrProductCoreOwnedNamespaces()
    {
        var contractsAssembly = typeof(Nexus.Platform.Contracts.Core.IQuotaPolicy).Assembly;

        var result = Types.InAssembly(contractsAssembly)
            .That()
            .ResideInNamespace("Nexus.Platform.Contracts.Core")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Nexus.Platform.Contracts.Models",
                "Nexus.Platform.Contracts.ProductCore",
                "Nexus.Platform.Contracts.Tools",
                "Nexus.Platform.Contracts.Governance",
                "Nexus.Products")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Nexus.Platform.Contracts.Core has a forbidden dependency on a non-neutral namespace: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    // The quota policy IMPLEMENTATION is conceptually L06 Product Core. It may depend
    // on the neutral L01 Core contract it implements, but must not reach into any
    // AI-owned namespace to do so (that would reintroduce the L06 -> L04 problem the
    // InvocationIdentity move fixed).
    [Fact]
    public void ProductCoreQuotaImplementation_MustNotDependOn_AiOwnedNamespaces()
    {
        var coreAssembly = typeof(Nexus.Platform.Core.ProductCore.PermissiveQuotaPolicy).Assembly;

        var result = Types.InAssembly(coreAssembly)
            .That()
            .ResideInNamespace("Nexus.Platform.Core.ProductCore")
            .ShouldNot()
            .HaveDependencyOnAny("Nexus.Platform.Contracts.Models", "Nexus.Platform.Core.Models")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Product-Core-owned quota implementation has a forbidden dependency on AI-owned code: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    // Positive companions to the two negative tests above: AI and Product Core must
    // each still be able to legitimately depend on Core -- proving the neutral
    // boundary actually gets used, not merely that forbidden edges are absent (a
    // namespace nobody references would pass the ShouldNot tests trivially too).
    //
    // Fixed after Windows verification found AiProvider_MayLegitimatelyDependOn_Core
    // failing (9/10 architecture tests passed; this was the one failure -- see
    // architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md). Root cause: NetArchTest's
    // namespace-scoped ".Should().HaveDependencyOnAny(...)" requires EVERY type in
    // the matched namespace to satisfy the dependency, not just one of them.
    // Nexus.Platform.Providers.OpenAI holds four types (OpenAIModelGateway,
    // OpenAIModelCatalogSource, OpenAIOptions, OpenAIServiceCollectionExtensions),
    // and only the gateway actually needs IQuotaPolicy -- so the aggregate check
    // failed even though the real, intended dependency exists and is correct. Both
    // positive tests below were replaced with direct structural/reflection
    // assertions instead of a namespace-aggregate NetArchTest check, per the
    // Windows-verification follow-up instruction.
    [Fact]
    public void AiProvider_MayLegitimatelyDependOn_Core()
    {
        var constructor = typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway)
            .GetConstructors()
            .Single();

        var hasQuotaPolicyParameter = constructor.GetParameters()
            .Any(p => p.ParameterType == typeof(Nexus.Platform.Contracts.Core.IQuotaPolicy));

        Assert.True(
            hasQuotaPolicyParameter,
            "Expected OpenAIModelGateway's constructor to take an IQuotaPolicy parameter.");
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.IQuotaPolicy).Namespace);
    }

    [Fact]
    public void ProductCoreImplementation_MayLegitimatelyDependOn_Core()
    {
        var implementsQuotaPolicy = typeof(Nexus.Platform.Core.ProductCore.PermissiveQuotaPolicy)
            .GetInterfaces()
            .Contains(typeof(Nexus.Platform.Contracts.Core.IQuotaPolicy));

        Assert.True(implementsQuotaPolicy, "Expected PermissiveQuotaPolicy to implement IQuotaPolicy.");
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.IQuotaPolicy).Namespace);
    }

    // Batch 07 (see architecture/NEXUS_V2_EXECUTION_BATCH_07_REPORT.md): the
    // pre-existing OpenAIModelGateway -> IAuditLog dependency was a real L04 AI ->
    // L03 Governance architectural dependency (matrix cell "-"), same shape as the
    // Batch 06 quota-policy defect, and is now fixed by reclassifying audit as
    // CORE-owned rather than Governance-owned (see AuditLog_IsOwnedByCoreLayer).
    // This is the negative half of that fix, enforced continuously: no type in the
    // OpenAI provider assembly may depend on the (now audit-free) Governance
    // namespace -- catching a future accidental reintroduction, e.g. if a real
    // IProductRegistry implementation appears there and something in AI reaches for
    // it directly instead of going through a lower-layer contract.
    [Fact]
    public void AiProviderAssembly_MustNotHaveTypeDependencyOn_GovernanceOwnedNamespaces()
    {
        var openAiAssembly = typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway).Assembly;

        var result = Types.InAssembly(openAiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Nexus.Platform.Contracts.Governance", "Nexus.Platform.Core.Governance")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "AI provider has a forbidden type-level dependency on Governance-owned code: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    // Positive companion, direct/structural rather than a namespace-aggregate
    // NetArchTest check (see AiProvider_MayLegitimatelyDependOn_Core above for why
    // that shape of check is unreliable when the namespace holds more than one
    // type -- Nexus.Platform.Providers.OpenAI does): confirms OpenAIModelGateway
    // still legitimately emits audit records through the CORE-owned IAuditLog port.
    [Fact]
    public void AiProvider_MayLegitimatelyEmitThrough_CoreAuditBoundary()
    {
        var constructor = typeof(Nexus.Platform.Providers.OpenAI.OpenAIModelGateway)
            .GetConstructors()
            .Single();

        var hasAuditLogParameter = constructor.GetParameters()
            .Any(p => p.ParameterType == typeof(Nexus.Platform.Contracts.Core.IAuditLog));

        Assert.True(
            hasAuditLogParameter,
            "Expected OpenAIModelGateway's constructor to take an IAuditLog parameter.");
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.IAuditLog).Namespace);
    }

    // C07-7 item 3 asked for a test proving "Governance audit implementation may
    // legitimately depend on Core". Source evidence (see AuditLog_IsOwnedByCoreLayer)
    // showed the audit IMPLEMENTATION, not only the contract, is CORE-owned per
    // LAYER_MODEL.md's own gate criteria -- so there is no longer a Governance-owned
    // audit implementation to test. This is the closest true analog: the concrete
    // implementation (now Core-owned) actually implements the Core-owned contract,
    // proven directly rather than by namespace-aggregate dependency.
    [Fact]
    public void ConsoleAuditLog_ImplementsTheCoreOwnedAuditContract()
    {
        var implementsAuditLog = typeof(Nexus.Platform.Core.ConsoleAuditLog)
            .GetInterfaces()
            .Contains(typeof(Nexus.Platform.Contracts.Core.IAuditLog));

        Assert.True(implementsAuditLog, "Expected ConsoleAuditLog to implement IAuditLog.");
        Assert.Equal("Nexus.Platform.Contracts.Core", typeof(Nexus.Platform.Contracts.Core.IAuditLog).Namespace);
    }
}
