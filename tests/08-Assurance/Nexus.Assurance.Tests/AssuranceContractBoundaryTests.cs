using System.Reflection;
using Nexus.Assurance.Contracts;
using Xunit;

namespace Nexus.Assurance.Tests;

/// <summary>
/// Structural checks that encode the Wave-05A Lane C authority decisions for the L08
/// ASSURANCE evidence-reference seam:
///   - Nexus.Assurance.Contracts references no Nexus.* assembly (a contracts leaf; in
///     particular it must never reference Nexus.Developer.* — Assurance must not depend on
///     Developer);
///   - the public contract surface carries no verdict/result/pass member — a consumer may
///     reference an Assurance-held evidence record but can never self-declare an Assurance
///     PASS through this assembly.
/// </summary>
public sealed class AssuranceContractBoundaryTests
{
    private static List<string> NexusAssemblyReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Nexus.", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void AssuranceContracts_ReferencesNoNexusAssembly()
    {
        var references = NexusAssemblyReferences(typeof(AssuranceEvidenceId).Assembly);

        Assert.Empty(references);
    }

    [Fact]
    public void PublicContractSurface_CarriesNoVerdictOrResultMember()
    {
        var assembly = typeof(AssuranceEvidenceId).Assembly;

        var verdictLikeMembers = assembly.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => !(m is ConstructorInfo))
            .Select(m => m.Name)
            .Where(n => n.Contains("Verdict", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Pass", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Result", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Status", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Outcome", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(verdictLikeMembers);
    }
}
