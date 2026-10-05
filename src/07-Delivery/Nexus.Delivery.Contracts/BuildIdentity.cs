using System.Text;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// The governed inputs of a build, and the identity derived from them.
///
/// <para>
/// <b>Environment is deliberately absent, and its absence is the point of this type.</b> The Owner's
/// ruling and the W9 rule both say the same thing: one build, promoted unchanged, with environment
/// configuration supplied outside the artifact. If an environment name could reach this record it could
/// reach the build id, and two environments would then be two builds — which is the failure this stage
/// exists to prevent. There is no member for it, no constructor parameter for it, and a test asserts by
/// reflection that no member of this type mentions one. <i>If an environment changes the application
/// bytes, that is not the same build with different configuration — it is a different build</i>, and it
/// will produce a different id because the bytes it produced are different, not because anyone told the
/// identity about the environment.
/// </para>
///
/// <para>
/// <b>A dirty working tree is recorded, not refused here.</b> The split is deliberate and matches W9.1:
/// a type refuses what is structurally unrepresentable, and the gate refuses what is merely wrong. A
/// dirty tree is a fact about the world, so <see cref="SourceRevision.WorkingTreeIsDirty"/> carries it
/// and <c>BuildOnceGuard</c> refuses it with a typed reason. Throwing here would turn an operational
/// condition into a crash, and would leave no record of what was attempted.
/// </para>
/// </summary>
public sealed record BuildIdentity
{
    public BuildIdentity(
        DeploymentUnitId unitId,
        IReadOnlyList<SourceRevision> sources,
        string buildDefinitionVersion,
        DependencyLockState dependencyLock,
        ToolchainIdentity toolchain,
        string buildConfiguration)
    {
        ArgumentNullException.ThrowIfNull(unitId);
        ArgumentNullException.ThrowIfNull(dependencyLock);
        ArgumentNullException.ThrowIfNull(toolchain);

        if (sources is null || sources.Count == 0)
        {
            throw new ArgumentException("A build identity must name at least one governed source revision.", nameof(sources));
        }

        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
        }

        var duplicate = sources
            .GroupBy(s => s.RepositoryLabel, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"A build identity names one revision per repository; '{duplicate.Key}' appears {duplicate.Count()} times.",
                nameof(sources));
        }

        if (!CredentialShape.IsNameShaped(buildDefinitionVersion))
        {
            throw new ArgumentException(
                "A build definition version must be name-shaped (e.g. 'build-definition-v1').",
                nameof(buildDefinitionVersion));
        }

        if (!CredentialShape.IsNameShaped(buildConfiguration))
        {
            throw new ArgumentException(
                "A build configuration must be name-shaped (e.g. 'Release').",
                nameof(buildConfiguration));
        }

        UnitId = unitId;
        Sources = [.. sources.OrderBy(s => s.RepositoryLabel, StringComparer.Ordinal)];
        BuildDefinitionVersion = buildDefinitionVersion;
        DependencyLock = dependencyLock;
        Toolchain = toolchain;
        BuildConfiguration = buildConfiguration;

        InputDigest = ComputeInputDigest();
        BuildId = BuildId.FromInputDigest(InputDigest);
    }

    public DeploymentUnitId UnitId { get; }

    /// <summary>Every governed repository revision consumed by this build, ordered by repository label.</summary>
    public IReadOnlyList<SourceRevision> Sources { get; }

    /// <summary>Version of the build definition (the pipeline/recipe). Changing the recipe is a different build.</summary>
    public string BuildDefinitionVersion { get; }

    public DependencyLockState DependencyLock { get; }

    public ToolchainIdentity Toolchain { get; }

    /// <summary>e.g. <c>Release</c>. Part of identity because it changes the output bytes.</summary>
    public string BuildConfiguration { get; }

    /// <summary>SHA-256 over the canonical form of every governed input.</summary>
    public ArtifactDigest InputDigest { get; }

    /// <summary>The derived build id. Two builds of identical inputs share it; nothing else does.</summary>
    public BuildId BuildId { get; }

    public bool HasDirtySource => Sources.Any(s => s.WorkingTreeIsDirty);

    /// <summary>
    /// The canonical form the digest is taken over. Deterministic by construction: sorted sources, fixed
    /// field order, and a NUL separator after every field so that adjacent values cannot be read as a
    /// single different value (<c>["ab","c"]</c> must not hash the same as <c>["a","bc"]</c>).
    /// </summary>
    public string CanonicalForm()
    {
        var builder = new StringBuilder();

        void Field(string? value) => builder.Append(value ?? string.Empty).Append('\0');

        Field("nexus-build-identity");
        Field(UnitId.Value);
        Field(BuildConfiguration);
        Field(BuildDefinitionVersion);
        Field(Toolchain.ToString());
        Field(DependencyLock.ToString());

        foreach (var source in Sources)
        {
            Field(source.ToString());
        }

        return builder.ToString();
    }

    private ArtifactDigest ComputeInputDigest()
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalForm()));

    public override string ToString() => $"{BuildId} ({UnitId}, {Sources.Count} source(s))";
}
