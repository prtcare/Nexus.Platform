namespace Nexus.Delivery.Contracts;

/// <summary>
/// A unit's release-surface declaration. Held in source beside the code it describes, so the
/// release surface is reviewable in a diff rather than discovered at deploy time.
///
/// <para>
/// W9.0 measured why a declaration is needed rather than a convention: four of the estate's five
/// services had <b>no defined deployment medium</b>, three of five had no readiness signal distinct
/// from liveness, and "build from source" did not identify a tree at all — 82 worktrees, 428
/// uncommitted changes, no repository on a release branch. Every one of those is a question this
/// record forces someone to answer in writing.
/// </para>
///
/// <para>
/// The obligations are enforced in the constructor, not in review. A <see cref="DeploymentUnitKind.Service"/>
/// with no readiness path is refused at construction, because a promotion gate that cannot tell
/// "up" from "ready" is not a gate.
/// </para>
/// </summary>
public sealed record DeploymentUnit
{
    public DeploymentUnit(
        DeploymentUnitId unitId,
        DeploymentUnitKind kind,
        string repositoryLabel,
        string projectPath,
        PackagingMedium packagingMedium,
        HealthContract? health = null,
        IReadOnlyList<string>? configurationKeys = null,
        IReadOnlyList<SecretReference>? secretReferences = null,
        IReadOnlyList<DeploymentUnitId>? dependsOn = null,
        MigrationMetadata? migrations = null,
        string? exclusionReason = null)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        if (string.IsNullOrWhiteSpace(repositoryLabel))
        {
            throw new ArgumentException("A unit must name the repository it is built from.", nameof(repositoryLabel));
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException(
                "A unit must name its PROJECT FILE, not a directory. Directories move; the project file is the buildable thing.",
                nameof(projectPath));
        }

        if (packagingMedium == PackagingMedium.Undeclared)
        {
            throw new ArgumentException(
                "A unit must declare its packaging medium. An undeclared medium is a deployment surprise; it must fail here instead.",
                nameof(packagingMedium));
        }

        if (kind == DeploymentUnitKind.Service && health is null)
        {
            throw new ArgumentException(
                "A service unit must declare a readiness signal (HealthContract).",
                nameof(health));
        }

        var expectedMedium = kind switch
        {
            DeploymentUnitKind.Service => (PackagingMedium?)null, // any declared medium is acceptable
            DeploymentUnitKind.Client => PackagingMedium.StaticBundle,
            DeploymentUnitKind.Library => PackagingMedium.Package,
            _ => null
        };

        if (expectedMedium is not null && packagingMedium != expectedMedium)
        {
            throw new ArgumentException(
                $"A {kind} unit must use the {expectedMedium} packaging medium, not {packagingMedium}.",
                nameof(packagingMedium));
        }

        var dependencies = dependsOn ?? [];
        if (dependencies.Contains(unitId))
        {
            throw new ArgumentException("A unit cannot depend on itself.", nameof(dependsOn));
        }

        UnitId = unitId;
        Kind = kind;
        RepositoryLabel = repositoryLabel;
        ProjectPath = projectPath;
        PackagingMedium = packagingMedium;
        Health = health;
        ConfigurationKeys = [.. configurationKeys ?? []];
        SecretReferences = [.. secretReferences ?? []];
        DependsOn = [.. dependencies];
        Migrations = migrations ?? MigrationMetadata.None;
        ExclusionReason = string.IsNullOrWhiteSpace(exclusionReason) ? null : exclusionReason;
    }

    public DeploymentUnitId UnitId { get; }

    public DeploymentUnitKind Kind { get; }

    /// <summary>Estate label, e.g. <c>AI/Intelligence</c>. Matches the W9.0 inventory's repository labels.</summary>
    public string RepositoryLabel { get; }

    /// <summary>Repository-relative path to the <c>.csproj</c> (or equivalent manifest).</summary>
    public string ProjectPath { get; }

    public PackagingMedium PackagingMedium { get; }

    /// <summary>Required for a service; null otherwise.</summary>
    public HealthContract? Health { get; }

    /// <summary>Every key this unit reads that may differ by environment. Names only.</summary>
    public IReadOnlyList<string> ConfigurationKeys { get; }

    /// <summary>Reference names. The <see cref="SecretReference"/> type makes a value unrepresentable here.</summary>
    public IReadOnlyList<SecretReference> SecretReferences { get; }

    public IReadOnlyList<DeploymentUnitId> DependsOn { get; }

    public MigrationMetadata Migrations { get; }

    /// <summary>
    /// Set when this unit must never be released, with the reason. A unit excluded without a stated
    /// reason is indistinguishable from one forgotten, so the reason is not optional.
    /// </summary>
    public string? ExclusionReason { get; }

    public bool IsExcluded => ExclusionReason is not null;
}
