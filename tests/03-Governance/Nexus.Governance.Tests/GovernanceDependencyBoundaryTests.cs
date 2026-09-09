using System.Reflection;
using Nexus.Governance.Contracts;
using Nexus.Governance.Core;
using Xunit;

namespace Nexus.Governance.Tests;

/// <summary>
/// Structural checks that encode the locked SP1-P01 dependency decisions for the L03
/// GOVERNANCE leaf assembly set:
///   - Nexus.Governance.Contracts references no Nexus.* assembly (it is a contracts leaf);
///   - Nexus.Governance.Core references ONLY Nexus.Governance.Contracts among Nexus.*
///     assemblies (never Nexus.Platform.Contracts, Nexus.Platform.Core or Nexus.ProductCore.*,
///     because CORE &lt;-&gt; GOVERNANCE references are forbidden and GOVERNANCE may not reach
///     into SHARED PLATFORM's physical ProductCore projects for this seed).
/// </summary>
public sealed class GovernanceDependencyBoundaryTests
{
    private static List<string> NexusAssemblyReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Nexus.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void GovernanceContracts_ReferencesNoNexusAssembly()
    {
        var references = NexusAssemblyReferences(typeof(IProductRegistry).Assembly);

        Assert.Empty(references);
    }

    [Fact]
    public void GovernanceCore_ReferencesOnlyGovernanceContracts_AmongNexusAssemblies()
    {
        var references = NexusAssemblyReferences(typeof(ProductRegistryService).Assembly);

        Assert.Equal(["Nexus.Governance.Contracts"], references);
    }
}
