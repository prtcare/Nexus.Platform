using System.Reflection;
using Nexus.Delivery.Contracts;
using Nexus.Delivery.Core;
using Xunit;

namespace Nexus.Delivery.Tests;

/// <summary>
/// Structural checks encoding the dependency decisions for the L07 DELIVERY leaf assembly set —
/// mirroring <c>GovernanceDependencyBoundaryTests</c>, which does the same for L03.
///
///   - <c>Nexus.Delivery.Contracts</c> references no Nexus.* assembly (it is a contracts leaf);
///   - <c>Nexus.Delivery.Core</c> references ONLY <c>Nexus.Delivery.Contracts</c> among Nexus.*
///     assemblies — never Nexus.Platform.Contracts, Nexus.Platform.Core, Nexus.Governance.* or
///     Nexus.ProductCore.*.
///
/// The second assertion is the one that keeps the delivery contracts usable from every head. A
/// registry abstraction that quietly took a dependency on Platform's core, or on GOVERNANCE, could not
/// be consumed by a host that does not compose those — and would have to be changed the moment a
/// second head wired it.
/// </summary>
public sealed class DeliveryDependencyBoundaryTests
{
    private static List<string> NexusAssemblyReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Nexus.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void DeliveryContracts_ReferencesNoNexusAssembly()
    {
        var references = NexusAssemblyReferences(typeof(ReleaseBundle).Assembly);

        Assert.Empty(references);
    }

    [Fact]
    public void DeliveryCore_ReferencesOnlyDeliveryContracts_AmongNexusAssemblies()
    {
        var references = NexusAssemblyReferences(typeof(DeploymentStateMachine).Assembly);

        Assert.Equal(["Nexus.Delivery.Contracts"], references);
    }

    /// <summary>
    /// The registry abstraction must stay cloud-portable: no member may name a vendor. This is asserted
    /// on the INTERFACE's surface rather than by reading it, so a future adapter that was tempted to add
    /// an overload taking a storage-specific handle fails here rather than silently narrowing the
    /// contract to one backend.
    /// </summary>
    [Fact]
    public void ArtifactRegistryContract_ExposesNoVendorVocabulary()
    {
        string[] vendorWords = ["azure", "aws", "amazon", "gcp", "google", "blob", "s3", "docker", "oci", "github", "acr"];

        var surface = typeof(IArtifactRegistry)
            .GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType.Name).Append(m.Name).Append(m.ReturnType.Name))
            .ToList();

        var leaked = surface
            .Where(n => vendorWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            leaked.Count == 0,
            "The artifact-registry contract names a vendor, so it is no longer portable: " + string.Join(", ", leaked));
    }

    /// <summary>
    /// Non-vacuity companion to the two negative tests above. A namespace nobody references would pass
    /// them trivially — the estate's recurring false-safety-guard defect — so this asserts the positive
    /// fact that the Core assembly really does implement the Contracts interface it is supposed to.
    /// </summary>
    [Fact]
    public void DeliveryCore_ImplementsTheDeliveryContractsBoundary()
    {
        Assert.Contains(
            typeof(IArtifactRegistry),
            typeof(FileArtifactRegistry).GetInterfaces());

        Assert.Contains(
            typeof(IDeploymentLineageLog),
            typeof(FileDeploymentLineageLog).GetInterfaces());

        Assert.Equal("Nexus.Delivery.Contracts", typeof(IArtifactRegistry).Assembly.GetName().Name);
    }
}
