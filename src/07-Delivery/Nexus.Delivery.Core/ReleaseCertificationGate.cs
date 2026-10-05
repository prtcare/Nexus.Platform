using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The release certification gate: the one place a Release Bundle becomes eligible to be deployed.
///
/// <para>
/// <b>Pure and total.</b> No I/O, no clock — it judges evidence that has already been gathered. That is
/// what makes a certification reproducible from its record: anyone holding the release bundle can re-run
/// this gate and get the same answer, which is the property that makes <c>READY_FOR_DEV</c> mean something
/// rather than being a label someone applied. Same shape and same reasoning as <see cref="CertificationGate"/>
/// one level down.
/// </para>
///
/// <para>
/// <b>Three verdicts, not two.</b> The technical conditions and the security actions are judged separately,
/// because they fail for different reasons and are remedied by different people. A release whose every
/// technical condition passes but whose credential rotation is outstanding is
/// <see cref="ReleaseCertificationVerdict.CertifiedPendingSecurityAction"/> — complete and waiting, not
/// broken. Folding that into a refusal would hide the fact that nothing about the release needs to change.
/// </para>
///
/// <para>
/// <b>The bundle is judged, not the manifest.</b> Two of the checks read the record's own contents rather
/// than the evidence — whether it carries everything TASK 2 requires, and whether anything
/// environment-shaped reached it. A caller cannot pass those by asserting them.
/// </para>
/// </summary>
public static class ReleaseCertificationGate
{
    /// <summary>The passing verdict value, matching <see cref="CertificationGate.CertifiedVerdict"/>'s convention.</summary>
    public const string CertifiedVerdict = "RELEASE_CERTIFIED";

    /// <summary>The verdict value when only a security action stands between the release and deployment.</summary>
    public const string PendingSecurityActionVerdict = "READY_FOR_DEV_PENDING_SECURITY_ACTION";

    public static ReleaseCertificationDecision Evaluate(ReleaseRecord release, ReleaseGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(evidence);

        var reasons = new List<ReleaseRefusalReason>();
        var detail = new List<string>();

        void Refuse(ReleaseRefusalReason reason, string message)
        {
            reasons.Add(reason);
            detail.Add(message);
        }

        // ---- 1. The release record's own completeness ------------------------------------------------
        // Read from the record, not from evidence: a caller must not be able to assert this away.
        foreach (var missing in MissingBundleElements(release))
        {
            Refuse(
                ReleaseRefusalReason.ReleaseBundleIncomplete,
                $"The release bundle is missing: {missing}.");
        }

        if (release.SchemaVersion < ReleaseRecord.MinSchemaVersion || release.SchemaVersion > ReleaseRecord.MaxSchemaVersion)
        {
            Refuse(
                ReleaseRefusalReason.ReleaseSchemaUnsupported,
                $"The release record's schema version {release.SchemaVersion} is outside {ReleaseRecord.MinSchemaVersion}-{ReleaseRecord.MaxSchemaVersion}.");
        }

        // ---- 2. Configuration boundary ---------------------------------------------------------------
        // Also read from the record. A value where a name belongs is an L-1 breach, not a warning.
        if (!ConfigurationBoundaryIsClean(release, out var boundaryDetail))
        {
            Refuse(ReleaseRefusalReason.ConfigurationValueInRelease, boundaryDetail);
        }

        // ---- 3. The certified build and its artifact -------------------------------------------------
        Check(evidence.BuildIsCertified, ReleaseRefusalReason.BuildNotCertified,
            "The build the release names was not certified, so there is no certified artifact to release.");

        Check(evidence.SourceLineageComplete, ReleaseRefusalReason.ReleaseBundleIncomplete,
            "The governed source lineage is incomplete, so nothing ties the release to a commit.");

        Check(evidence.ArtifactHashMatchesManifest, ReleaseRefusalReason.ArtifactHashMismatch,
            "The artifact's stored bytes do not hash to the digest the build manifest recorded. "
            + "A mismatch here means the store's contents changed, which is a corruption finding.");

        // ---- 4. Evidence the build already established -----------------------------------------------
        Check(evidence.ReproducibilityEstablished, ReleaseRefusalReason.ReproducibilityNotEstablished,
            "Reproducibility is not established for the build this release consumes. "
            + "A single build agreeing with itself is not reproducibility.");

        Check(evidence.TestsPassed, ReleaseRefusalReason.TestsNotPassed,
            "The required tests did not pass.");

        Check(evidence.DependencyManifestAvailable, ReleaseRefusalReason.DependencyManifestMissing,
            "No dependency manifest is available, and its absence is not recorded as an unlocked state.");

        Check(evidence.ContractCompatibilityAcceptable, ReleaseRefusalReason.ContractCompatibilityUnacceptable,
            "A pinned contract version is incompatible with what the release requires.");

        Check(evidence.HealthDefinitionPresent, ReleaseRefusalReason.HealthDefinitionMissing,
            "No health definition exists, so no promoter can read a readiness signal.");

        Check(evidence.ConfigurationSchemaKnown, ReleaseRefusalReason.ReleaseBundleIncomplete,
            "The configuration schema this release expects is not known.");

        // ---- 5. Migration and rollback state ---------------------------------------------------------
        Check(evidence.MigrationStateKnown, ReleaseRefusalReason.MigrationStateUnknown,
            "Whether a database migration applies could not be established. "
            + "Promoting on an unmeasured migration risk is the failure this gate exists to prevent.");

        Check(evidence.MigrationBackupEstablished, ReleaseRefusalReason.MigrationBackupNotEstablished,
            "A migration applies and the target must be backed up first; the requirement is recorded but not established.");

        Check(evidence.RollbackStateKnown, ReleaseRefusalReason.RollbackReferenceFabricated,
            "The rollback reference is not established.");

        // ---- 6. The governed release reference -------------------------------------------------------
        Check(evidence.ReleaseRefGoverned, ReleaseRefusalReason.ReleaseRefNotGoverned,
            "The release reference is absent, is not an annotated tag, or does not carry this release's governed identity.");

        // ---- 7. Security gates over the active release input -----------------------------------------
        Check(evidence.SecretScanPassed, ReleaseRefusalReason.SecretScanFindingsInActiveInput,
            "An active release input carries a secret finding. Rotate first "
            + "(CONFIGURATION_STANDARDS.md 13.2); removing a file does not remove it from history.");

        Check(evidence.ConfigurationBoundaryClean, ReleaseRefusalReason.EnvironmentSpecificValueInArtifact,
            "An environment-specific value reached the artifact or the release, so reaching another "
            + "environment would require a rebuild.");

        if (reasons.Count > 0)
        {
            return ReleaseCertificationDecision.Refuse(release.ReleaseId, [.. reasons], detail);
        }

        // ---- 8. The security actions ---------------------------------------------------------------
        // Reached only when every technical condition passed. These do not refuse; they name what is
        // outstanding, so that the release lands in its own typed state rather than in a generic failure.
        var outstanding = new List<string>();

        if (evidence.CredentialRotationConfirmed is not true)
        {
            outstanding.Add(
                evidence.CredentialRotationConfirmed is null
                    ? "CREDENTIAL_ROTATION_UNCONFIRMED: the W9.0 committed-credential exposure has not been recorded as rotated or revoked."
                    : "CREDENTIAL_ROTATION_OUTSTANDING: the W9.0 committed-credential exposure is recorded as not yet rotated or revoked.");
        }

        // The release reference's protection is judged for ENV-DEV, from the one judgement that knows about
        // mechanisms and scopes. It is deliberately not a boolean test here: the recorded mechanism may be
        // server-side protection, or the Owner-approved C-2 compensating control, and the two are not
        // interchangeable — the substitution is DEV/TEST-only, and the deployment gate re-asks this same
        // question with the environment it is actually about to change.
        var protection = evidence.ReleaseReferenceProtection.Judge(DeploymentEnvironmentId.DevEnv);

        if (!protection.IsSatisfied)
        {
            outstanding.Add(protection.Detail);
        }

        if (outstanding.Count > 0)
        {
            return ReleaseCertificationDecision.CertifiedPendingSecurityAction(
                release.ReleaseId,
                outstanding,
                [
                    "Every technical condition for DEV readiness is met.",
                    "The release is NOT deployable and NOT externally publishable until the listed security actions complete.",
                    .. outstanding
                ]);
        }

        // How the reference is protected is recorded ON the certification rather than implied by it. A
        // certification that said only "certified" would leave a reader unable to tell a release backed by a
        // ruleset from one backed by the C-2 substitution, and the compensating control must never be read as
        // the control it replaced.
        return ReleaseCertificationDecision.Certified(release.ReleaseId, [protection.Detail]);

        void Check(bool? observed, ReleaseRefusalReason reason, string message)
        {
            // Null is "not observed", never "fine". The estate's rule: a thing that could not be checked is
            // reported as not run, and never as a pass.
            if (observed is null)
            {
                Refuse(ReleaseRefusalReason.EvidenceIncomplete, $"{message} (Not observed.)");
            }
            else if (observed == false)
            {
                Refuse(reason, message);
            }
        }
    }

    /// <summary>
    /// The members TASK 2 requires that are absent or unusable. Judged in combination, which is why it is
    /// not the constructor's job: the constructor makes each member non-null, and this decides whether what
    /// is there is enough.
    /// </summary>
    public static IReadOnlyList<string> MissingBundleElements(ReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);

        var missing = new List<string>();

        if (release.Artifacts.Count == 0)
        {
            missing.Add("artifactId");
        }

        if (release.Artifacts.Any(a => a.ContentDigest is null))
        {
            missing.Add("artifactHash");
        }

        if (release.Artifacts.Any(a => a.SizeBytes <= 0))
        {
            missing.Add("artifactSize");
        }

        if (string.IsNullOrWhiteSpace(release.BuildId.Value))
        {
            missing.Add("buildId");
        }

        if (release.Identity.SourceCommits.Count == 0)
        {
            missing.Add("sourceCommits");
        }

        if (string.IsNullOrWhiteSpace(release.Identity.ReleaseRefName))
        {
            missing.Add("releaseReference");
        }

        if (release.ContractVersions.Count == 0)
        {
            missing.Add("contractVersions");
        }

        if (string.IsNullOrWhiteSpace(release.ConfigurationSchemaVersion))
        {
            missing.Add("configurationSchemaVersion");
        }

        if (string.IsNullOrWhiteSpace(release.Health.ReadinessPath))
        {
            missing.Add("healthDefinition");
        }

        if (string.IsNullOrWhiteSpace(release.Evidence.BuildManifestReference))
        {
            missing.Add("buildEvidenceReference");
        }

        if (string.IsNullOrWhiteSpace(release.Evidence.TestSuiteName))
        {
            missing.Add("testEvidenceReference");
        }

        if (string.IsNullOrWhiteSpace(release.Evidence.BuilderRunId))
        {
            missing.Add("provenanceReference");
        }

        // The dependency manifest must be *answered for*: either locked, or unlocked with a stated reason.
        // DependencyLockState makes the unlocked note mandatory, so a state with no answer cannot exist.
        if (release.DependencyLock is null)
        {
            missing.Add("dependencyManifest");
        }

        return missing;
    }

    /// <summary>
    /// True when nothing environment-shaped or credential-shaped reached the release.
    ///
    /// <para>
    /// The judgement is structural, not a search for known values: configuration appears as key names and
    /// secrets as reference names, and both types enforce name shape at construction. So this checks the one
    /// thing the types cannot — that the release does not carry a <i>value</i> under a key-shaped label, and
    /// that no artifact identity smuggled an endpoint into a name or version.
    /// </para>
    /// </summary>
    public static bool ConfigurationBoundaryIsClean(ReleaseRecord release, out string detail)
    {
        ArgumentNullException.ThrowIfNull(release);

        // Deliberately NOT re-checking configuration keys or artifact names for name shape. Both are already
        // refused by their own types — ReleaseRecord's constructor and ArtifactId.For respectively — so a
        // loop here could never fire, and this estate's standing finding is that a guard which cannot fail
        // is worse than no guard: it reads as protection while establishing nothing. What is checked below
        // is the member whose type does NOT constrain it.
        foreach (var version in release.ContractVersions)
        {
            // The contract NAME is name-shaped; the version is a version, and a version is not a name —
            // "0.1.0" legitimately begins with a digit and would fail a name check. What a version must not
            // be is credential-shaped, which is the same judgement the secret scanner makes, applied here so
            // that a value cannot be smuggled through the one member that is not name-constrained.
            if (!CredentialShape.IsNameShaped(version.ContractName) || CredentialShape.LooksLikeCredentialValue(version.Version))
            {
                detail = $"Contract '{version.ContractName}' carries a version that is not a version.";
                return false;
            }
        }

        detail = "No environment endpoint, no configuration value and no secret value is carried by the release.";
        return true;
    }
}
