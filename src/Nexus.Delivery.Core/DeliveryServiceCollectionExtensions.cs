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
}
