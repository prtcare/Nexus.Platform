using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The build-once rules, as refusals.
///
/// <para>
/// Three properties, each checked mechanically rather than asserted in a document:
/// </para>
///
/// <list type="number">
/// <item><b>An environment cannot change application bytes through normal promotion.</b> Checked twice —
/// once against the build identity (no environment-shaped input may reach it) and once against the
/// promotion attempt (it may not ask for a rebuild).</item>
/// <item><b>Changed artifact hash means a new build.</b> Checked by comparing the digest presented for an
/// existing release id against the certified digest.</item>
/// <item><b>A promotion may not replace the artifact behind an existing release id.</b> Checked by
/// requiring a certified digest to reference — a promotion that brings bytes of its own is refused.</item>
/// </list>
///
/// <para>
/// <b>Why <see cref="BuildOnceRefusalReason.EnvironmentInBuildIdentity"/> is a real check and not a
/// guard that cannot fail.</b> <see cref="BuildIdentity"/> has no environment member, so an environment
/// can never be passed to it <i>as</i> an environment — but nothing stops one being smuggled through a
/// free-text field, and the obvious candidate is the build configuration. A pipeline that sets
/// <c>-c Release-ENV-PROD</c> would produce two builds that differ only by environment and would look
/// perfectly valid. This method inspects the values that reach the identity and refuses that, which is a
/// check with teeth precisely because the type-level guarantee does not cover it.
/// </para>
/// </summary>
public static class BuildOnceGuard
{
    /// <summary>
    /// Inspects a build identity for environment-shaped inputs. Empty means the identity carries nothing
    /// environment-dependent.
    /// </summary>
    public static IReadOnlyList<(BuildOnceRefusalReason Reason, string Detail)> InspectIdentity(BuildIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var findings = new List<(BuildOnceRefusalReason, string)>();

        void Check(string what, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var environment in DeploymentEnvironmentId.All)
            {
                // Match on the ratified identifier and on its bare tail, so both "ENV-PROD" and a
                // "Release-PROD" style suffix are caught. Case-insensitive: the label is not the point,
                // the dependence is.
                if (value.Contains(environment.Value, StringComparison.OrdinalIgnoreCase)
                    || value.Contains(environment.Value.Replace("ENV-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add((
                        BuildOnceRefusalReason.EnvironmentInBuildIdentity,
                        $"{what} contains the environment identifier '{environment.Value}'. An environment must not reach the build identity: two environments would then be two builds."));
                    return;
                }
            }
        }

        Check("The build configuration", identity.BuildConfiguration);
        Check("The build definition version", identity.BuildDefinitionVersion);
        Check("The toolchain container image", identity.Toolchain.ContainerImage);
        Check("The dependency lock note", identity.DependencyLock.Note);

        foreach (var source in identity.Sources)
        {
            Check($"The source ref for '{source.RepositoryLabel}'", source.RepositoryLabel);
        }

        return findings;
    }

    /// <summary>
    /// Evaluates a promotion attempt against all three rules.
    /// </summary>
    public static BuildOnceVerdict Evaluate(BuildIdentity identity, PromotionAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(attempt);

        var refusals = new List<(BuildOnceRefusalReason, string)>();

        refusals.AddRange(InspectIdentity(identity));

        if (attempt.RequestsRebuild)
        {
            refusals.Add((
                BuildOnceRefusalReason.EnvironmentSpecificRebuildRequested,
                $"The promotion to {attempt.TargetEnvironment} asks for a rebuild. Promotion moves bytes; it does not produce them."));
        }

        if (!attempt.ArtifactIsCertified)
        {
            refusals.Add((
                BuildOnceRefusalReason.ArtifactNotCertified,
                $"Artifact '{attempt.ArtifactId}' is not certified, so it is not eligible to be promoted at all."));
        }

        if (attempt.CertifiedDigest is null)
        {
            refusals.Add((
                BuildOnceRefusalReason.ArtifactSuppliedRatherThanReferenced,
                "No certified digest was supplied, so the attempt is offering its own bytes. A promotion references a published artifact; it does not bring one."));
        }
        else if (attempt.PresentedDigest is not null && attempt.PresentedDigest != attempt.CertifiedDigest)
        {
            refusals.Add((
                BuildOnceRefusalReason.DigestChangedForExistingReleaseId,
                $"Release id '{attempt.ReleaseId}' holds {attempt.CertifiedDigest}, but {attempt.PresentedDigest} was presented. "
                + "Changed bytes always mean a NEW build, never a promotion."));
        }

        return refusals.Count == 0 ? BuildOnceVerdict.Permit() : BuildOnceVerdict.Refuse([.. refusals]);
    }
}
