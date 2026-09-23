using Microsoft.Extensions.DependencyInjection;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Registration for the delivery contracts' implementations. Mirrors the shape
/// <c>AddNexusGovernance</c> uses in this repository, so a host wires delivery the same way it wires
/// governance.
///
/// <para>
/// <b>Roots are supplied, not discovered.</b> The file-backed registry and lineage log both need a
/// filesystem location, and neither guesses one — a component that picks its own storage location is a
/// component whose data appears somewhere nobody chose. The caller names the roots; the host decides
/// where they live.
/// </para>
///
/// <para>
/// The <see cref="SecretScanPolicy"/> is registered as an instance rather than a type, because a policy
/// is data. A repository that needs a vendor-specific pattern registers <c>Neutral</c> plus its own
/// patterns here — which is how a provider-specific rule stays out of this neutral assembly.
/// </para>
/// </summary>
public static class DeliveryServiceCollectionExtensions
{
    public static IServiceCollection AddNexusDelivery(
        this IServiceCollection services,
        string artifactRegistryRoot,
        string lineageLogRoot,
        SecretScanPolicy? secretScanPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRegistryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(lineageLogRoot);

        services.AddSingleton<IArtifactRegistry>(_ => new FileArtifactRegistry(artifactRegistryRoot));
        services.AddSingleton<IDeploymentLineageLog>(_ => new FileDeploymentLineageLog(lineageLogRoot));
        services.AddSingleton(secretScanPolicy ?? SecretScanPolicy.Neutral);
        services.AddSingleton<SecretScanner>();
        services.AddSingleton<DeploymentPromoter>();

        return services;
    }

    /// <summary>
    /// Registers the W9.2 build-and-artifact components.
    ///
    /// <para>
    /// Separate from <see cref="AddNexusDelivery"/> because the two halves have different lifetimes in
    /// practice: a deployment host needs the promotion half, while a build agent needs this one, and only
    /// a certification runner needs both. Keeping them separable means a build agent does not acquire a
    /// registry it has no business publishing to.
    /// </para>
    ///
    /// <para>
    /// The artifact store root is supplied rather than discovered, for the same reason as the others — and
    /// here it matters more, because this store holds the bytes a release is made of.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNexusBuildAndArtifacts(
        this IServiceCollection services,
        string artifactStoreRoot,
        IProcessRunner? processRunner = null,
        SecretScanPolicy? secretScanPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactStoreRoot);

        services.AddSingleton<IArtifactStore>(_ => new FileArtifactStore(artifactStoreRoot));
        services.AddSingleton(secretScanPolicy ?? SecretScanPolicy.Neutral);
        services.AddSingleton(processRunner ?? new ProcessRunner());
        services.AddSingleton<BuildInputScanner>();
        services.AddSingleton<BuildOrchestrator>(provider => new BuildOrchestrator(
            provider.GetRequiredService<IProcessRunner>(),
            artifactType => artifactType switch
            {
                ArtifactType.Package => new PreBuiltFilePackager(),
                ArtifactType.ContainerImage => new PreBuiltFilePackager(ArtifactType.ContainerImage),
                _ => new ZipDirectoryPackager(artifactType)
            }));

        return services;
    }

    /// <summary>
    /// Registers the W9.3 release components: assembling a Release Bundle from a certified artifact,
    /// registering it immutably, creating its governed release reference, and recording its lineage.
    ///
    /// <para>
    /// <b>Separate from the build half, and the separation is load-bearing.</b> A host that can release must
    /// not be a host that can build: the whole point of this stage is that a release consumes bytes produced
    /// exactly once, and a release host that also carried a compiler would be one configuration mistake away
    /// from rebuilding them. So this method registers no packager, no orchestration and no build step — it
    /// registers only the components that read certified output and describe it.
    /// </para>
    ///
    /// <para>
    /// The roots are supplied rather than discovered, for the same reason as every other root in this file.
    /// The release registry shares the artifact store's root by default, so that a release, the artifacts it
    /// names and the build indexes that join them live in one place; a caller may separate them, and doing so
    /// is a decision rather than an accident.
    /// </para>
    /// </summary>
    /// <param name="artifactStoreRoot">
    /// Where certified artifacts live. Under the W9 proof this is the path named by
    /// <c>NEXUS_ARTIFACT_STORE_ROOT</c>; it is passed in rather than read here, so that this assembly never
    /// depends on an environment variable being set.
    /// </param>
    /// <param name="releaseRegistryRoot">Where release records live. Defaults to the artifact store root.</param>
    /// <param name="releaseLineageRoot">Where the release lineage ledger lives.</param>
    /// <param name="serverSideProtectionVerified">
    /// Whether an authorized check has confirmed the remote ruleset protecting the release-tag namespace.
    /// <b>Defaults to false.</b> A local process cannot observe a remote ruleset, so the honest value until
    /// someone verifies it is "not verified", and a caller that has verified it says so explicitly here.
    /// </param>
    public static IServiceCollection AddNexusRelease(
        this IServiceCollection services,
        string artifactStoreRoot,
        string releaseLineageRoot,
        string? releaseRegistryRoot = null,
        IProcessRunner? processRunner = null,
        bool serverSideProtectionVerified = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactStoreRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseLineageRoot);

        var registryRoot = string.IsNullOrWhiteSpace(releaseRegistryRoot) ? artifactStoreRoot : releaseRegistryRoot;

        // The artifact store is needed to verify a certified artifact's hash before a release names it, so it
        // is registered here too rather than assumed to have been registered by the build half.
        services.AddSingleton<IArtifactStore>(_ => new FileArtifactStore(artifactStoreRoot));
        services.AddSingleton<IReleaseRegistry>(_ => new FileReleaseRegistry(registryRoot));
        services.AddSingleton<IReleaseLineageLog>(_ => new FileReleaseLineageLog(releaseLineageRoot));
        services.AddSingleton(processRunner ?? new ProcessRunner());
        services.AddSingleton<IReleaseTagPolicy>(provider => new GitReleaseTagPolicy(
            provider.GetRequiredService<IProcessRunner>(),
            serverSideProtectionVerified));
        services.AddSingleton<IReleaseTagPublisher>(provider => new GitReleaseTagPublisher(
            provider.GetRequiredService<IProcessRunner>(),
            provider.GetRequiredService<IReleaseTagPolicy>()));
        services.AddSingleton<ReleaseBundleAssembler>();
        services.AddSingleton<ReleaseLineageResolver>();

        return services;
    }
}
