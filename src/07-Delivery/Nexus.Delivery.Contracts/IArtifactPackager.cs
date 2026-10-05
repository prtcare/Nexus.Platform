namespace Nexus.Delivery.Contracts;

/// <summary>
/// Packages a built output into an artifact.
///
/// <para>
/// <b>The obligation that matters is the negative one.</b> A packager must not inject environment
/// configuration, an endpoint, a secret reference or a secret value into what it packages. W9.0 found two
/// units in this estate where exactly that happened — the Experience client's <c>VITE_*</c> values and
/// MarketSurvey's <c>API_INTERNAL_URL</c> build argument, which Next.js resolves into
/// <c>.next/routes-manifest.json</c> and carries into the standalone server. A packager that could do the
/// same thing would make the build-once rule unenforceable from the outside, so the contract is stated as
/// a requirement the implementation must be able to demonstrate, not as advice.
/// </para>
///
/// <para>
/// <see cref="PackageAsync"/> receives a staging directory that already contains the built output. It
/// must not modify the source tree, and it must not reach outside the staging root — a packager that
/// reads ambient environment variables is a packager whose output cannot be reproduced.
/// </para>
/// </summary>
public interface IArtifactPackager
{
    /// <summary>Which artifact type this packager produces.</summary>
    ArtifactType ArtifactType { get; }

    /// <summary>
    /// Packages <paramref name="stagingDirectory"/> into <paramref name="outputDirectory"/> and returns
    /// what was produced. Returns a refusal rather than throwing when the *input* is unusable, so the
    /// caller can record why.
    /// </summary>
    Task<PackageOutcome> PackageAsync(
        PackageRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>What to package, and as what.</summary>
public sealed record PackageRequest(
    ArtifactId ArtifactId,
    BuildId BuildId,
    string StagingDirectory,
    string OutputDirectory);

/// <summary>The outcome of packaging. A refusal is an ordinary result, not an exception.</summary>
public sealed record PackageOutcome
{
    private PackageOutcome(bool isPackaged, PackagedArtifact? artifact, string? refusalReason)
    {
        IsPackaged = isPackaged;
        Artifact = artifact;
        RefusalReason = refusalReason;
    }

    public bool IsPackaged { get; }

    public PackagedArtifact? Artifact { get; }

    /// <summary>Operator-facing reason. Must never contain a secret value.</summary>
    public string? RefusalReason { get; }

    public static PackageOutcome Packaged(PackagedArtifact artifact) => new(true, artifact, null);

    public static PackageOutcome Refused(string reason) => new(false, null, reason);
}
