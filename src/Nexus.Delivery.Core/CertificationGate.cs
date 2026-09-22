using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The certification gate: the one place an artifact becomes eligible to be released.
///
/// <para>
/// <b>Pure and total.</b> No I/O, no clock — it judges evidence that has already been gathered. That is
/// what makes a certification reproducible from its manifest: anyone holding the record can re-run this
/// gate and get the same answer, which is the property that makes "ARTIFACT_CERTIFIED" mean something
/// rather than being a label someone applied.
/// </para>
///
/// <para>
/// <b>Every refusal is typed</b> (<see cref="CertificationRefusalReason"/>), so a caller can act on the
/// specific deficiency rather than parse prose. The gate collects every failing condition rather than
/// returning at the first one: a build that fails four checks should say so once, not four times over four
/// attempts.
/// </para>
/// </summary>
public static class CertificationGate
{
    /// <summary>The single passing verdict value.</summary>
    public const string CertifiedVerdict = "ARTIFACT_CERTIFIED";

    public static CertificationDecision Evaluate(CertificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var manifest = evidence.Manifest;
        var reasons = new List<CertificationRefusalReason>();
        var detail = new List<string>();

        void Refuse(CertificationRefusalReason reason, string message)
        {
            reasons.Add(reason);
            detail.Add(message);
        }

        // ---- 1. Governed source lineage -------------------------------------------------------
        if (manifest.Identity.Sources.Count == 0
            || manifest.Identity.Sources.Any(s => string.IsNullOrWhiteSpace(s.CommitSha)))
        {
            Refuse(
                CertificationRefusalReason.GovernedSourceLineageMissing,
                "No governed source revision is recorded, so nothing ties the artifact to a commit.");
        }

        if (manifest.Identity.HasDirtySource)
        {
            var dirty = manifest.Identity.Sources.Where(s => s.WorkingTreeIsDirty).Select(s => s.RepositoryLabel);
            Refuse(
                CertificationRefusalReason.SourceWorkingTreeIsDirty,
                $"Uncommitted changes in: {string.Join(", ", dirty)}. The commit does not describe the bytes that were built.");
        }

        // ---- 2. The build itself ---------------------------------------------------------------
        if (!evidence.BuildSucceeded)
        {
            Refuse(CertificationRefusalReason.BuildFailed, "The build did not succeed.");
        }

        // ---- 3. Required tests -----------------------------------------------------------------
        // Three distinct outcomes, three distinct reasons — because the remedy differs. A failed suite is a
        // code problem; a suite that did not run is a pipeline problem; a suite with nothing in it is a
        // coverage problem. Reporting all three as "tests not green" loses the part that tells you what to do.
        switch (manifest.Tests.Verdict)
        {
            case TestVerdict.Failed:
                Refuse(
                    CertificationRefusalReason.TestsFailed,
                    $"{manifest.Tests.SuiteName}: {manifest.Tests.Failed} of {manifest.Tests.Total} tests failed.");
                break;

            case TestVerdict.NotRun:
                Refuse(
                    CertificationRefusalReason.TestsNotRun,
                    $"{manifest.Tests.SuiteName} was not executed. A suite that did not run is NOT_RUN, never a pass.");
                break;

            case TestVerdict.NoTests:
                Refuse(
                    CertificationRefusalReason.TestsAbsent,
                    $"{manifest.Tests.SuiteName} contains no tests. A suite with nothing to run is NO_TESTS, never a pass.");
                break;
        }

        // ---- 4. Secret scan, over the whole input set ------------------------------------------
        if (manifest.SecretScan.Verdict == SecretScanVerdict.Findings)
        {
            Refuse(
                CertificationRefusalReason.SecretScanFindingsInActiveInput,
                $"The secret scan reported {manifest.SecretScan.FindingLocations.Count} finding(s) in active build input. "
                + "Rotate first (CONFIGURATION_STANDARDS.md 13.2); removing the file does not remove it from history.");
        }

        if (manifest.SecretScan.Verdict == SecretScanVerdict.Incomplete)
        {
            Refuse(
                CertificationRefusalReason.SecretScanIncomplete,
                "The secret scan could not complete, so the input set has not been established as clean.");
        }

        var expected = evidence.ExpectedActiveInputLabels.OrderBy(l => l, StringComparer.Ordinal).ToList();
        var covered = manifest.SecretScan.ScannedSubjectLabels.OrderBy(l => l, StringComparer.Ordinal).ToList();
        var uncovered = expected.Except(covered, StringComparer.Ordinal).ToList();

        if (uncovered.Count > 0)
        {
            Refuse(
                CertificationRefusalReason.SecretScanIncompleteCoverage,
                $"The scan did not cover: {string.Join(", ", uncovered)}. A build is only as clean as the inputs nobody scanned.");
        }

        // ---- 5. Manifest completeness ----------------------------------------------------------
        var missing = manifest.MissingRequiredElements();
        if (missing.Count > 0)
        {
            Refuse(CertificationRefusalReason.ManifestIncomplete, $"Manifest is missing: {string.Join(", ", missing)}.");
        }

        if (manifest.Artifacts.Any(a => a.ContentDigest is null))
        {
            Refuse(CertificationRefusalReason.ArtifactHashMissing, "An artifact carries no content hash.");
        }

        // ---- 6. Reproducibility ----------------------------------------------------------------
        if (!manifest.Reproducibility.IsProven)
        {
            Refuse(
                CertificationRefusalReason.ReproducibilityNotProven,
                $"Reproducibility is {manifest.Reproducibility.Verdict} across {manifest.Reproducibility.BuildsCompared} build(s). "
                + "A single build agreeing with itself is not reproducibility.");
        }

        // ---- 7. Environment neutrality ---------------------------------------------------------
        if (evidence.UnitRequiresEnvironmentSpecificRebuild)
        {
            Refuse(
                CertificationRefusalReason.EnvironmentSpecificRebuildDependency,
                "The unit's build still compiles an environment-dependent value in, so reaching another environment would require a rebuild.");
        }

        if (reasons.Count > 0)
        {
            return CertificationDecision.Refuse(manifest.BuildId, [.. reasons], detail);
        }

        return CertificationDecision.Certified(manifest.BuildId);
    }
}
