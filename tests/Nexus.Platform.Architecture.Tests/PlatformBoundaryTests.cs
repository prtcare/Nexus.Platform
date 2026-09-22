using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Nexus.Platform.Architecture.Tests;

public sealed class PlatformBoundaryTests
{
    // W5G / F-01: this list is deliberately EXHAUSTIVE over the Platform assemblies that
    // remain. It previously also named Nexus.Platform.Providers.OpenAI, which moved to the
    // AI Head; its boundary assertions moved with it to
    // Nexus.Intelligence.Architecture.Tests. A Platform assembly NOT listed here is a
    // silent hole in every test that iterates this array (Platform_MustNotReference_
    // IntelligenceOrProducts, Platform_MustNotReference_Governance), so adding a project
    // to Nexus.Platform.slnx means adding it here too.
    private static readonly Assembly[] PlatformAssemblies =
    [
        typeof(Nexus.Platform.Contracts.Models.ModelDescriptor).Assembly,
        typeof(Nexus.Platform.Core.PlatformServiceCollectionExtensions).Assembly,

        // W9.1: the L07 DELIVERY leaf set -- the deployment contracts and their deterministic Core
        // implementations. Added here because the paragraph above is a standing obligation, not an
        // observation: every test in this file iterates this array, so a Platform assembly that is
        // not named here is a hole in the PRODUCT boundary, the GOVERNANCE boundary and the
        // forbidden-product-name check at the same time.
        typeof(Nexus.Delivery.Contracts.ReleaseBundle).Assembly,
        typeof(Nexus.Delivery.Core.DeploymentStateMachine).Assembly,

        // W9.2: the build-and-certify driver. A tool that lives in the solution and is not named here is a
        // hole in the PRODUCT, GOVERNANCE and forbidden-name checks at once -- and this one reads whole
        // source trees, so it is a poor candidate for being trusted on its own word.
        typeof(Nexus.Delivery.Build.Program).Assembly
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
    // See architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md. Renamed in Batch 09:
    // this asserts CORE-owned model/provider infrastructure placement, not an "AI
    // layer" -- see architecture/NEXUS_V2_EXECUTION_BATCH_08_REPORT.md and
    // _BATCH_09_REPORT.md for why "Layer 04 AI" never applied to this code (Layer 04
    // AI is Nexus.Intelligence.*, a separate repository).
    //
    // W5G / F-01 SUPERSEDES THE ORIGINAL INTENT, deliberately and in one direction only.
    // The original test also asserted
    //   Assert.Equal("Nexus.Platform.Core.Models",
    //                typeof(Nexus.Platform.Core.Models.InMemoryUsageMeter).Namespace);
    // which locked InMemoryUsageMeter into this repository. F-01 moved the whole model
    // domain -- IModelCatalog, AggregatingModelCatalog, IModelCatalogSource,
    // INamedModelGateway, InMemoryUsageMeter, RoutingModelGateway -- to the AI Head as
    // AI-domain semantics, so that assertion is now false by design and has been removed
    // rather than weakened. The CONTRACTS stay here: the neutral metering contract
    // (IUsageMeter/UsageRecord) is what Platform ships and what the PL-04 Contract Plane
    // boundary is made of. What Platform keeps, and what this test now locks in, is that
    // boundary -- not the implementation behind it. The AI-side ownership of
    // InMemoryUsageMeter is asserted from the AI Head, where the type now lives.
    [Fact]
    public void UsageMeter_ContractIsOwnedByPlatformNeutralContracts()
    {
        Assert.Equal("Nexus.Platform.Contracts.Models", typeof(Nexus.Platform.Contracts.Models.IUsageMeter).Namespace);
        Assert.Equal("Nexus.Platform.Contracts.Models", typeof(Nexus.Platform.Contracts.Models.UsageRecord).Namespace);
        Assert.Equal(
            "Nexus.Platform.Contracts",
            typeof(Nexus.Platform.Contracts.Models.IUsageMeter).Assembly.GetName().Name);
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
    //
    // Batch 09 terminology correction: everywhere above that says "L04 AI"/"AI"
    // describes what is actually L01 CORE-owned provider/model infrastructure
    // (Nexus.Platform.Providers.OpenAI, Contracts.Models, Core.Models). L04 AI is
    // Nexus.Intelligence.* in a separate repository and was never involved in this
    // boundary. The dependency-direction analysis and fix above remain correct; only
    // the layer label was wrong -- see
    // architecture/NEXUS_V2_EXECUTION_BATCH_08_REPORT.md and _BATCH_09_REPORT.md.
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

    // W5G / F-01 RELOCATIONS. Four assertions used to sit here, all scoped to
    // Nexus.Platform.Providers.OpenAI:
    //
    //   OpenAiProviderAssembly_MustNotHaveTypeDependencyOn_ProductCoreOwnedNamespaces
    //   OpenAiProvider_MayLegitimatelyDependOn_Core
    //   OpenAiProviderAssembly_MustNotHaveTypeDependencyOn_GovernanceOwnedNamespaces
    //   OpenAiProvider_MayLegitimatelyEmitThrough_CoreAuditBoundary
    //
    // The provider moved to the AI Head, so nothing in this repository can reference it
    // and these four cannot compile here. They were MOVED, not deleted: they now live in
    // Nexus.Intelligence.Architecture.Tests/RelocatedProviderBoundaryTests.cs, in the
    // same change. Deleting them instead would have removed the only continuous guard on
    // the provider's Product-Core and GOVERNANCE independence, and the two positive
    // companions that keep the negative tests from passing vacuously -- the repository's
    // recurring false-safety-guard defect. The relocated copies assert the same
    // relationships; what changed is the assembly they are compiled against and the fact
    // that the "Core" they may legitimately depend on is now consumed as a package from
    // the cross-Head contract boundary rather than as a sibling project.

    // Added in the InvocationIdentity follow-up correction: the neutral CORE boundary
    // only stays neutral if nothing inside it reaches back up into a layer that
    // depends on it. If a future edit reintroduced a provider/model-infrastructure-
    // owned, Product-Core-owned, or GOVERNANCE-owned type dependency into
    // Nexus.Platform.Contracts.Core, this fails immediately rather than waiting for a
    // human to notice during the next physical move. Renamed in Batch 09: the forbidden
    // namespaces checked below are all non-neutral relative to Contracts.Core; none of
    // them is "L04 AI" -- see architecture/NEXUS_V2_EXECUTION_BATCH_08_REPORT.md.
    // M-03-1.2 (SP1-P01) removed the old Governance namespace from this assembly
    // (IProductRegistry relocated to the Nexus.Governance.* leaf assemblies), so the
    // GOVERNANCE guard is now the "Nexus.Governance" prefix: Contracts.Core must never
    // reach up into L03 GOVERNANCE in any physical assembly.
    [Fact]
    public void ContractsCoreNamespace_MustNotDependOn_NonCoreNeutralNamespaces()
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
                "Nexus.Governance",
                "Nexus.Products")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Nexus.Platform.Contracts.Core has a forbidden dependency on a non-neutral namespace: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    // The quota policy IMPLEMENTATION is conceptually L06 Product Core. It may depend
    // on the neutral L01 Core contract it implements, but must not reach into the
    // CORE-owned model/provider infrastructure namespaces (Contracts.Models,
    // Core.Models) to do so -- reaching into either would reintroduce an L06 -> L01
    // dependency on infrastructure the quota policy has no business calling directly.
    // Renamed in Batch 09: this was never an "L06 -> L04" problem -- both namespaces
    // checked below are L01 CORE, not L04 AI -- see
    // architecture/NEXUS_V2_EXECUTION_BATCH_08_REPORT.md.
    //
    // W5G / F-01: the "Nexus.Platform.Core.Models" half of the check was DROPPED, not
    // because the rule changed but because the namespace no longer exists in this
    // repository -- PermissiveQuotaPolicy can no longer reach it here even in principle.
    // NetArchTest does not fail an unknown namespace, so leaving it in would have been a
    // permanently-vacuous half of a real check: the exact false-safety-guard shape this
    // estate keeps re-discovering. "Nexus.Platform.Contracts.Models" is kept as the
    // check that still has teeth, because that namespace is still shipped by this
    // repository (as a package).
    [Fact]
    public void ProductCoreQuotaImplementation_MustNotDependOn_CoreModelInfrastructureNamespaces()
    {
        var coreAssembly = typeof(Nexus.Platform.Core.ProductCore.PermissiveQuotaPolicy).Assembly;

        var result = Types.InAssembly(coreAssembly)
            .That()
            .ResideInNamespace("Nexus.Platform.Core.ProductCore")
            .ShouldNot()
            .HaveDependencyOnAny("Nexus.Platform.Contracts.Models")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Product-Core-owned quota implementation has a forbidden dependency on model-infrastructure code: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    // Positive companion to the negative tests above: Product Core must still be able to
    // legitimately depend on Core -- proving the neutral boundary actually gets used, not
    // merely that forbidden edges are absent (a namespace nobody references would pass
    // the ShouldNot tests trivially too). Its former twin,
    // OpenAiProvider_MayLegitimatelyDependOn_Core, moved to the AI Head with the provider.
    //
    // The OpenAI provider's own positive/negative pair was originally written as a
    // namespace-aggregate NetArchTest check and had to be rewritten as direct reflection
    // (see architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md): NetArchTest's
    // namespace-scoped ".Should().HaveDependencyOnAny(...)" requires EVERY type in the
    // matched namespace to satisfy the dependency, and the provider's four types do not
    // all take an IQuotaPolicy. The relocated copies keep that corrected shape.
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
    // pre-existing OpenAIModelGateway -> IAuditLog dependency was a real forbidden
    // architectural dependency (matrix cell "-"), same shape as the Batch 06
    // quota-policy defect, and is now fixed by reclassifying audit as CORE-owned
    // rather than Governance-owned (see AuditLog_IsOwnedByCoreLayer). Batch 07's own
    // text called this "L04 AI -> L03 Governance"; Batch 08/09 determined that label
    // was wrong -- OpenAIModelGateway is L01 CORE-owned provider infrastructure, not
    // L04 AI (L04 AI is Nexus.Intelligence.*, a separate repository) -- so the actual
    // relationship fixed was L01 CORE -> L03 Governance. The dependency-direction fix
    // itself was correct either way; only the layer label was wrong (see
    // architecture/NEXUS_V2_EXECUTION_BATCH_08_REPORT.md and _BATCH_09_REPORT.md).
    //
    // W5G / F-01: the negative half of that fix,
    // OpenAiProviderAssembly_MustNotHaveTypeDependencyOn_GovernanceOwnedNamespaces, and
    // its positive companion OpenAiProvider_MayLegitimatelyEmitThrough_CoreAuditBoundary,
    // moved to the AI Head with the provider. The equivalent GOVERNANCE guard on the
    // assemblies that remain is Platform_MustNotReference_Governance below, which
    // iterates PlatformAssemblies and therefore still enforces the same edge for
    // Contracts and Core.

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

    // M-03-1.2 (SP1-P01) work item WI-03-1.2.2 / subtask S-03-1.2.2.1.2: a layering test
    // asserting L01 CORE (Nexus.Platform.*) does not reference L03 GOVERNANCE
    // (Nexus.Governance.*). Product identity has relocated out of Nexus.Platform.Contracts
    // into its own GOVERNANCE leaf assembly set; CORE must never take a reference on it
    // (DEPENDENCY_RULES.md row 01 CORE has no "may reference" cell for column 03
    // GOVERNANCE). This guards the CORE -> GOVERNANCE direction across every Platform
    // assembly the repository ships today.
    [Fact]
    public void Platform_MustNotReference_Governance()
    {
        foreach (var assembly in PlatformAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Nexus.Governance")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{assembly.GetName().Name} has a forbidden dependency on GOVERNANCE: " +
                string.Join(", ", result.FailingTypeNames ?? []));
        }
    }
}
