using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The pre-deployment release-reference verification: rules 4–9 of the Owner-approved C-2 compensating
/// control, decided in one place, before a deployment is allowed to move bytes.
///
/// <para>
/// <b>What it is for.</b> A downstream repository cannot enforce a ruleset on this estate's repositories, so
/// the release tag is not the authoritative release identity — the certified chain
/// (<c>SourceCommit → BuildId → ArtifactId + sha256 → ReleaseId → Release Bundle</c>) is, and the tag is a
/// published pointer to it. A pointer that is <i>trusted</i> reintroduces exactly the failure the missing
/// ruleset would have prevented. So every deployment re-derives the chain and checks the pointer against it,
/// rather than reading the pointer and believing it. That inversion is the whole compensating control.
/// </para>
///
/// <para>
/// <b>The three questions, each answered where it is already answered.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Is the local reference a governed release reference for this release?</b> — asked of
/// <see cref="IReleaseTagPolicy"/>, unchanged. The governed-block parsing, the moved-tag detection and the
/// "a branch is not an immutable reference" judgement all live there and are not re-derived here. That
/// covers rules 1–3 and rule 8's prohibition on trusting a name.
/// </description></item>
/// <item><description>
/// <b>Do the stored artifact bytes hash to the digest this release records?</b> — asked of
/// <see cref="IArtifactStore"/>, once per artifact the release names. This is rule 6, and it is what makes
/// rule 8 structural rather than aspirational: the bytes are checked against the <i>release record</i>, so
/// there is no code path in which the tag's agreement could stand in for a hash match. Rule 8 is therefore
/// not a separate check that could be forgotten — it is the absence of a check that could substitute.
/// </description></item>
/// <item><description>
/// <b>Does the remote hold that same governed reference, at the certified source?</b> — asked of
/// <see cref="IRemoteReleaseTagVerifier"/>, which is the only capability here that did not exist before.
/// This is rules 4, 5, 7 and 9.
/// </description></item>
/// </list>
///
/// <para>
/// <b>Server-side protection is not claimed and not collapsed.</b> The local assessment reports
/// <see cref="ReleaseRefProtectionStatus.RequiresServerSideVerification"/> whenever no authorized check has
/// observed a remote ruleset, and that is the truthful value on this estate's repositories — no such
/// mechanism is available on them. This gate therefore admits a reference on
/// <see cref="ReleaseTagAssessment.IsGoverned"/>, which is the judgement that excludes server-side
/// protection deliberately, and it records the protection status it observed rather than restating it as
/// <see cref="ReleaseRefProtectionStatus.Protected"/>. The substitution is the Owner's recorded deviation
/// (C-2, Option 4), and a substituted control that reported itself as the original would be the precise
/// overstatement the deviation exists to avoid.
/// </para>
///
/// <para>
/// <b>The artifact digest is read from the release, never from the tag.</b> A verified tag carries artifact
/// identities in its governed block, and those are recorded evidence — useful for a reader, useless as a
/// check, because a tag that was re-pointed carries whatever its re-pointer wrote. Rule 6 compares the store
/// to the release record, and the release record's digest is the one the registry enforces immutability
/// against.
/// </para>
/// </summary>
public sealed class ReleaseRefPreDeploymentGate
{
    private readonly IReleaseTagPolicy _policy;
    private readonly IRemoteReleaseTagVerifier _verifier;
    private readonly IArtifactStore _store;

    public ReleaseRefPreDeploymentGate(
        IReleaseTagPolicy policy,
        IRemoteReleaseTagVerifier verifier,
        IArtifactStore store)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Runs the pre-deployment verification. Never throws for a refusal: every outcome a deployment must
    /// stop for is a typed state on the returned verdict.
    /// </summary>
    /// <param name="repositoryLabel">The repository as this estate names it, for evidence.</param>
    /// <param name="repositoryRoot">Where the governed release reference is read from.</param>
    /// <param name="remote">The remote to observe, as the caller names it. Never resolved or defaulted here.</param>
    /// <param name="release">The immutable Release Bundle being deployed.</param>
    public async Task<ReleaseRefPreDeploymentVerdict> VerifyAsync(
        string repositoryLabel,
        string repositoryRoot,
        string remote,
        ReleaseRecord release,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentNullException.ThrowIfNull(release);

        var refName = IReleaseTagPolicy.RefNameFor(release);

        // ---- Rule 6 (and rule 8 by construction): the bytes, against the release record -----------------
        // Run first, because it is the only check that can be decided without observing anything outside
        // this machine — and because a release whose own artifact bytes have changed is broken before any
        // question about a remote pointer is interesting.
        var artifactReasons = new List<ReleaseRefusalReason>();
        var artifactDetail = new List<string>();

        foreach (var artifact in release.Artifacts)
        {
            var verification = await _store
                .VerifyHashAsync(artifact.ArtifactId, artifact.ContentDigest, cancellationToken)
                .ConfigureAwait(false);

            if (!verification.IsMatch || !verification.StoreContentIntact)
            {
                artifactReasons.Add(ReleaseRefusalReason.ArtifactHashMismatch);
                artifactDetail.Add(
                    $"'{artifact.ArtifactId.Value}' records {artifact.ContentDigest} in the release bundle; {verification.Detail}");
            }
        }

        if (artifactReasons.Count > 0)
        {
            return ReleaseRefPreDeploymentVerdict.Refused(
                ReleaseRefPreDeploymentState.ArtifactBytesUnverified,
                remote,
                refName,
                CertifiedTargets(release),
                expectedTagObjectSha: null,
                observedRemoteTagObjectSha: null,
                observedRemoteTargetCommitSha: null,
                artifactReasons,
                [
                    "The artifact bytes do not hash to the digest the release bundle records, so the bytes a deployment would move are not the certified bytes.",
                    .. artifactDetail
                ]);
        }

        // ---- Rules 1-3: the local reference is this release's governed reference ------------------------
        // Delegated rather than re-derived. If this refuses, the message a caller sees is the assessor's own
        // typed reason, so the two can never disagree about WHY a reference is not governed.
        var local = await _policy
            .AssessAsync(repositoryLabel, repositoryRoot, release, cancellationToken)
            .ConfigureAwait(false);

        if (!local.IsGoverned)
        {
            // The typed reason at THIS level is ReleaseRefNotGoverned, and the tag-level reason is carried in
            // the detail rather than translated. The two vocabularies are deliberately separate — the tag
            // policy's reasons describe a tag, this verdict's describe a deployment — and inventing a mapping
            // between them would be a second judgement about what a tag-level reason means for a deployment,
            // which is the drift this estate keeps paying for. The tag policy's own reasons are named verbatim
            // so nothing is lost from the evidence.
            var tagReasons = local.RefusalReasons.Count > 0
                ? string.Join(", ", local.RefusalReasons)
                : "(the assessor named none)";

            return ReleaseRefPreDeploymentVerdict.Refused(
                ReleaseRefPreDeploymentState.ReferenceNotGoverned,
                remote,
                refName,
                CertifiedTargets(release),
                local.Descriptor?.TagObjectSha,
                observedRemoteTagObjectSha: null,
                observedRemoteTargetCommitSha: null,
                [ReleaseRefusalReason.ReleaseRefNotGoverned],
                [
                    $"'{repositoryLabel}:{refName}' is not this release's governed release reference, so there is nothing for the remote to agree with.",
                    $"The release-reference assessor's own typed reasons: {tagReasons}.",
                    $"Protection status observed locally: {local.Descriptor?.ProtectionStatus.ToString() ?? ReleaseRefProtectionStatus.Absent.ToString()}.",
                    .. local.Detail
                ]);
        }

        var expectedTagObjectSha = local.Descriptor?.TagObjectSha;
        var protectionStatus = local.Descriptor?.ProtectionStatus ?? ReleaseRefProtectionStatus.Absent;

        // ---- Rules 4, 5, 7, 9: what the remote holds, and whether it agrees -----------------------------
        var observation = await _verifier
            .ObserveAsync(remote, repositoryRoot, release, cancellationToken)
            .ConfigureAwait(false);

        var certifiedTargets = CertifiedTargets(release);

        switch (observation.State)
        {
            case RemoteReleaseTagObservationState.Unreachable:
                return ReleaseRefPreDeploymentVerdict.Refused(
                    ReleaseRefPreDeploymentState.RemoteUnreachable,
                    remote,
                    refName,
                    certifiedTargets,
                    expectedTagObjectSha,
                    observation.RemoteTagObjectSha,
                    observation.RemoteTargetCommitSha,
                    [ReleaseRefusalReason.RemoteReleaseRefUnreachable],
                    [
                        "The remote tag was not read, so rule 4's verification did not happen. Deployment stops, because 'could not check' is not 'checked'.",
                        .. observation.Detail
                    ]);

            case RemoteReleaseTagObservationState.Absent:
                // The deliberate choice the deviation names: absence is its own state. It is NOT drift —
                // nothing contradicted the chain — and it is NOT success — nothing was verified. A control
                // that reported absence as verified would be reporting a check that never ran, and one that
                // reported it as drift would cry attack on an ordinary stage boundary and train its readers
                // to ignore the drift signal that exists for the real case.
                return ReleaseRefPreDeploymentVerdict.Refused(
                    ReleaseRefPreDeploymentState.RemoteReferenceAbsent,
                    remote,
                    refName,
                    certifiedTargets,
                    expectedTagObjectSha,
                    observation.RemoteTagObjectSha,
                    observation.RemoteTargetCommitSha,
                    [ReleaseRefusalReason.RemoteReleaseRefAbsent],
                    [
                        "The release reference has not been published to the remote, so the remote's copy of it cannot be verified. Deployment stops until it is published.",
                        "This is the expected pre-publication state and is reported as absence rather than as drift; no remote ref was found that disagreed with the certified chain.",
                        .. observation.Detail
                    ]);

            case RemoteReleaseTagObservationState.Lightweight:
                return ReleaseRefPreDeploymentVerdict.Refused(
                    ReleaseRefPreDeploymentState.ReferenceDrift,
                    remote,
                    refName,
                    certifiedTargets,
                    expectedTagObjectSha,
                    observation.RemoteTagObjectSha,
                    observation.RemoteTargetCommitSha,
                    [ReleaseRefusalReason.ReleaseReferenceDrift],
                    [
                        "The remote holds a lightweight tag at the release reference name. It cannot carry the governed release identity, so it is not the reference the governed publisher wrote.",
                        .. observation.Detail
                    ]);

            case RemoteReleaseTagObservationState.Annotated:
                return JudgeAnnotatedObservation(observation);

            default:
                // A future member must fail closed rather than fall through to the verified path.
                return ReleaseRefPreDeploymentVerdict.Refused(
                    ReleaseRefPreDeploymentState.RemoteUnreachable,
                    remote,
                    refName,
                    certifiedTargets,
                    expectedTagObjectSha,
                    observation.RemoteTagObjectSha,
                    observation.RemoteTargetCommitSha,
                    [ReleaseRefusalReason.EvidenceIncomplete],
                    [$"The remote observation reported a state this gate does not understand: {observation.State}."]);
        }

        ReleaseRefPreDeploymentVerdict JudgeAnnotatedObservation(RemoteReleaseTagObservation observed)
        {
            var reasons = new List<ReleaseRefusalReason>();
            var detail = new List<string>();

            // Rule 5 — the remote reference must still point at the certified source.
            if (!ReleaseRefTargetRule.PointsAtACommitTheReleaseCarries(release, observed.RemoteTargetCommitSha))
            {
                reasons.Add(ReleaseRefusalReason.ReleaseReferenceDrift);
                detail.Add(
                    "The remote reference points at a commit this release was not built from: "
                    + ReleaseRefTargetRule.DescribeRefusal(release, observed.RemoteTargetCommitSha));
            }

            // Rules 3 and 7 — the remote must hold the same object the governed publisher wrote. This is the
            // check that catches a reference re-pointed on the remote by anyone holding push access, which is
            // exactly the adversary the missing ruleset would have stopped: a re-pointed tag keeps its name
            // and its target can be made to match, but it cannot keep its object id, because a tag object's
            // id covers its target and its message.
            //
            // MEASURED DOMINANCE, recorded here so a future reader does not overrate either half. A mutation
            // run disabled each check independently against the controls in PreDeploymentGateTests:
            //   - disable this object-identity check alone  -> no control fails (the target check catches both)
            //   - disable the target check above alone       -> no control fails (this check catches both)
            //   - disable BOTH                               -> both drift controls fail
            // For an annotated remote ref the two cannot disagree: a tag object's id covers its target, so
            // "same object" implies "same target" and a different target is always a different object. The
            // target check is therefore not independently load-bearing for annotated refs and is retained as
            // the statement of rule 5 in rule 5's own terms — and because it is the only check available when
            // the local object id could not be read. Neither check may be removed on the grounds that the
            // other "already covers it": that reasoning is exactly how rule 5 disappears in a refactor.
            if (expectedTagObjectSha is not null
                && !string.Equals(expectedTagObjectSha, observed.RemoteTagObjectSha, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(ReleaseRefusalReason.ReleaseReferenceDrift);
                detail.Add(
                    $"The remote holds tag object {observed.RemoteTagObjectSha}, but this release's governed reference is tag object {expectedTagObjectSha}. "
                    + "The remote's reference is not the one the governed publisher wrote.");
            }

            if (reasons.Count > 0)
            {
                return ReleaseRefPreDeploymentVerdict.Refused(
                    ReleaseRefPreDeploymentState.ReferenceDrift,
                    remote,
                    refName,
                    certifiedTargets,
                    expectedTagObjectSha,
                    observed.RemoteTagObjectSha,
                    observed.RemoteTargetCommitSha,
                    reasons,
                    [
                        $"{ReleaseRefPreDeploymentVerdict.ReleaseReferenceDriftVerdict}: the remote reference does not match the certified chain. Deployment stops.",
                        .. detail,
                        .. observed.Detail
                    ]);
            }

            return ReleaseRefPreDeploymentVerdict.Verified(
                remote,
                refName,
                certifiedTargets,
                expectedTagObjectSha,
                observed.RemoteTagObjectSha!,
                observed.RemoteTargetCommitSha!,
                [
                    $"'{repositoryLabel}:{refName}' is a governed annotated release reference and its target is a commit this release carries.",
                    $"The remote '{remote}' holds that same tag object ({observed.RemoteTagObjectSha}) peeling to {observed.RemoteTargetCommitSha}.",
                    $"The stored artifact bytes hash to the digests {release.ReleaseId} records.",
                    $"Server-side ruleset protection is NOT claimed: protection status observed was {protectionStatus}. "
                    + "The substitute is the Owner-approved C-2 compensating control (Option 4), scoped to ENV-DEV and ENV-TEST.",
                    "Observed remote tag sha recorded per rule 9."
                ]);
        }
    }

    /// <summary>
    /// The commits the remote reference is expected to point at: the certified source commits the release
    /// records, as a set.
    ///
    /// <para>
    /// <b>Supplied from the record rather than defaulted to a branch head or a remote default</b>, because a
    /// defaulted expectation is one that agrees with whatever it is compared to. <b>And a set rather than one
    /// commit</b>, because that is what <see cref="ReleaseRefTargetRule"/> enforces: a release built from two
    /// repositories has two source commits and one reference, so naming a single expected commit here would
    /// record evidence narrower than the check that was actually applied.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> CertifiedTargets(ReleaseRecord release) => release.Identity.SourceCommits;
}
