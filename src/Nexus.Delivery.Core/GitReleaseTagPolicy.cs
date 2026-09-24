using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Assesses and creates the governed release reference, as an <b>immutable annotated Git tag</b> in the
/// tag namespace, per the Owner's D-W9.3-01 ruling.
///
/// <para>
/// <b>Why the tag namespace and not a branch.</b> <see cref="GitReleaseRefPolicy"/> reads
/// <c>refs/heads/release</c>, which is one of its own conventions and today matches nothing in this estate.
/// A branch is a moving name: it is the thing a release must not be built from, because if any actor can
/// move the ref a release was built from, build-once is void (W8-DEBT-01, escalated). A tag object is
/// immutable once written — moving one means deleting it and writing another, which is observable, and an
/// observable move is one this mechanism can refuse.
/// </para>
///
/// <para>
/// <b>Annotated, not lightweight.</b> A lightweight tag is a bare pointer: it has no message, so it cannot
/// carry the governed identity block, so a later assessor cannot tell it from a tag someone made with the
/// right name. <see cref="ReleaseTagAnnotation"/> is that block, and this policy reads it back and compares
/// it to the release it was asked about. That is the whole difference between a naming convention and a
/// control.
/// </para>
///
/// <para>
/// <b>Protection is not inferred.</b> A local process can observe that its own publisher refuses to move a
/// tag. It cannot observe a remote ruleset — a repository with one and a repository without look identical
/// from a checkout. So the assessment reports
/// <see cref="ReleaseRefProtectionStatus.RequiresServerSideVerification"/> unless the caller supplies an
/// authorized confirmation, and the Owner's D2 requirement is thereby stated as behaviour rather than as an
/// intention.
/// </para>
///
/// <para>
/// <b>This writes nothing. Creation is <see cref="GitReleaseTagPublisher"/>'s job.</b> An assessor that
/// could create the thing it assesses would be able to satisfy its own check.
/// </para>
/// </summary>
public sealed class GitReleaseTagPolicy : IReleaseTagPolicy
{
    /// <summary>The governed tag namespace. A tag outside it is not a release reference, whatever it is called.</summary>
    public const string TagNamespace = "refs/tags/release/";

    private readonly IProcessRunner _runner;
    private readonly bool _serverSideProtectionVerified;

    /// <param name="runner">Process runner, injectable so this is testable without git.</param>
    /// <param name="serverSideProtectionVerified">
    /// Whether an authorized check has confirmed the remote ruleset protecting the release-tag namespace.
    /// <b>Defaults to false and must not be defaulted to true</b>: a local process cannot observe a ruleset,
    /// so the honest value until someone verifies it is "not verified". Recorded as a constructor input so
    /// that the claim is made once, explicitly, by whoever is entitled to make it.
    /// </param>
    public GitReleaseTagPolicy(IProcessRunner runner, bool serverSideProtectionVerified = false)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _serverSideProtectionVerified = serverSideProtectionVerified;
    }

    public Task<ReleaseTagAssessment> AssessAsync(
        string repositoryLabel,
        string repositoryRoot,
        ReleaseRecord release,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        return AssessRefAsync(repositoryLabel, repositoryRoot, IReleaseTagPolicy.RefNameFor(release), release, cancellationToken);
    }

    public async Task<ReleaseTagAssessment> AssessRefAsync(
        string repositoryLabel,
        string repositoryRoot,
        string refName,
        ReleaseRecord release,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(refName);
        ArgumentNullException.ThrowIfNull(release);

        var protection = _serverSideProtectionVerified
            ? ReleaseRefProtectionStatus.Protected
            : ReleaseRefProtectionStatus.RequiresServerSideVerification;

        var protectionMechanism = _serverSideProtectionVerified
            ? "Publisher refusal plus a verified remote ruleset on the release-tag namespace."
            : "Publisher refusal only; no remote ruleset was observed from this stage.";

        // ---- A branch is refused before anything is read --------------------------------------------
        // Refused first, and by name, because it is the substitution the temptation points at: a branch
        // called release/<unit>/<version> pointed at the right commit looks like the prerequisite is met.
        if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            return ReleaseTagAssessment.Ineligible(
                new ReleaseTagDescriptor(
                    repositoryLabel, refName, Exists: true, IsAnnotated: false, protection, ProtectionMechanism: protectionMechanism),
                [ReleaseTagRefusalReason.BranchCannotSubstituteForImmutableTag],
                [
                    $"'{refName}' is a branch. A branch moves, so a release built from one cannot be identified afterwards.",
                    "D-W9.3-01 requires an immutable annotated tag; a branch head is not one."
                ]);
        }

        if (!refName.StartsWith(TagNamespace, StringComparison.Ordinal))
        {
            return ReleaseTagAssessment.Ineligible(
                new ReleaseTagDescriptor(
                    repositoryLabel, refName, Exists: false, IsAnnotated: false, protection, ProtectionMechanism: protectionMechanism),
                [ReleaseTagRefusalReason.OutsideReleaseTagNamespace],
                [$"'{refName}' is outside the governed release-tag namespace '{TagNamespace}'."]);
        }

        // ---- Does it exist, and what kind of object is it? -------------------------------------------
        var typeResult = await _runner
            .RunAsync("git", ["cat-file", "-t", refName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (!typeResult.Succeeded)
        {
            return ReleaseTagAssessment.Ineligible(
                new ReleaseTagDescriptor(
                    repositoryLabel, refName, Exists: false, IsAnnotated: false, ReleaseRefProtectionStatus.Absent,
                    ProtectionMechanism: null),
                [ReleaseTagRefusalReason.TagAbsent],
                [
                    $"'{repositoryLabel}' has no tag at '{refName}'.",
                    "A release reference is created by the governed publisher, not by the assessor."
                ]);
        }

        var objectType = typeResult.StandardOutput.Trim();

        // git reports "tag" for an annotated tag object and "commit" for a lightweight tag, because a
        // lightweight tag IS the commit. That single word is the whole annotated/lightweight distinction,
        // and it is read from git rather than inferred from the tag's name.
        if (!string.Equals(objectType, "tag", StringComparison.Ordinal))
        {
            var peeled = await _runner.RunAsync("git", ["rev-list", "-n", "1", refName], repositoryRoot, cancellationToken)
                .ConfigureAwait(false);

            return ReleaseTagAssessment.Ineligible(
                new ReleaseTagDescriptor(
                    repositoryLabel, refName, Exists: true, IsAnnotated: false, protection,
                    PeeledCommitSha: peeled.Succeeded ? peeled.StandardOutput.Trim() : null,
                    ProtectionMechanism: protectionMechanism),
                [ReleaseTagRefusalReason.LightweightTagNotPermitted],
                [
                    $"'{refName}' is a lightweight tag (object type '{objectType}').",
                    "It carries no message, so it cannot carry the governed release identity and cannot be verified against the release it names."
                ]);
        }

        var tagObjectSha = await _runner.RunAsync("git", ["rev-parse", refName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);
        var peeledCommit = await _runner.RunAsync("git", ["rev-list", "-n", "1", refName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        // ---- Read the governed block back out of the tag's own message -------------------------------
        var contents = await _runner
            .RunAsync("git", ["for-each-ref", "--format=%(contents)", refName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (!contents.Succeeded || !ReleaseTagAnnotation.TryParse(contents.StandardOutput, out var annotation) || annotation is null)
        {
            return ReleaseTagAssessment.Ineligible(
                new ReleaseTagDescriptor(
                    repositoryLabel, refName, Exists: true, IsAnnotated: true, protection,
                    TagObjectSha: tagObjectSha.Succeeded ? tagObjectSha.StandardOutput.Trim() : null,
                    PeeledCommitSha: peeledCommit.Succeeded ? peeledCommit.StandardOutput.Trim() : null,
                    ProtectionMechanism: protectionMechanism),
                [ReleaseTagRefusalReason.TagAnnotationNotGoverned],
                [
                    $"'{refName}' is annotated but its message carries no governed release identity block.",
                    "A tag given the right name by something other than the governed publisher is a naming convention, not a release reference."
                ]);
        }

        var descriptor = new ReleaseTagDescriptor(
            repositoryLabel,
            refName,
            Exists: true,
            IsAnnotated: true,
            protection,
            TagObjectSha: tagObjectSha.Succeeded ? tagObjectSha.StandardOutput.Trim() : null,
            PeeledCommitSha: peeledCommit.Succeeded ? peeledCommit.StandardOutput.Trim() : null,
            AnnotatedReleaseId: annotation.ReleaseId,
            AnnotatedBuildId: annotation.BuildId,
            AnnotatedRecordDigest: annotation.RecordDigest,
            ProtectionMechanism: protectionMechanism);

        // ---- Does the block describe THIS release? ---------------------------------------------------
        if (annotation.ReleaseId != release.ReleaseId)
        {
            return ReleaseTagAssessment.Ineligible(
                descriptor,
                [ReleaseTagRefusalReason.TagIdentifiesDifferentRelease],
                [$"'{refName}' carries a governed block for {annotation.ReleaseId}, not {release.ReleaseId}."]);
        }

        // The release id matches, so any remaining disagreement is the record itself. This is what a moved
        // tag looks like: the tag was recreated, its annotation rewritten from the same identity, and the
        // only thing that did not survive is the digest of the record the reference was created for.
        if (!annotation.Describes(release))
        {
            return ReleaseTagAssessment.Ineligible(
                descriptor,
                [ReleaseTagRefusalReason.TagWasMoved],
                [
                    $"'{refName}' carries a block for {annotation.ReleaseId} whose record digest is {annotation.RecordDigest}, "
                    + $"but the release presented hashes to {release.ComputeRecordDigest()}.",
                    "The reference was recreated at different content. An immutable reference that can be re-pointed is not immutable."
                ]);
        }

        // ---- Does the tag actually contain the certified build's source? -----------------------------
        var tagCommit = peeledCommit.Succeeded ? peeledCommit.StandardOutput.Trim() : null;

        var targetCommit = release.Identity.SourceCommits.FirstOrDefault(c =>
            tagCommit is not null && string.Equals(c, tagCommit, StringComparison.Ordinal));

        if (tagCommit is null || targetCommit is null)
        {
            return ReleaseTagAssessment.Ineligible(
                descriptor,
                [ReleaseTagRefusalReason.TagDoesNotContainBuildCommit],
                [
                    $"'{refName}' peels to {(tagCommit ?? "(unreadable)")}, "
                    + "which is not among the commits the certified build recorded."
                ]);
        }

        // ---- Protection ------------------------------------------------------------------------------
        if (!_serverSideProtectionVerified)
        {
            return ReleaseTagAssessment.Ineligible(
                descriptor,
                [ReleaseTagRefusalReason.ServerSideProtectionUnverified],
                [
                    $"'{refName}' is a governed annotated release reference and contains the certified source.",
                    "Server-side ruleset protection of the release-tag namespace could not be observed from this stage, and is not assumed (D2).",
                    "Local refusal is in place: the governed publisher will not move or replace an existing release tag."
                ]);
        }

        return ReleaseTagAssessment.Eligible(descriptor);
    }
}

/// <summary>
/// Creates the governed release reference.
///
/// <para>
/// <b>The only thing in the estate that writes a release tag.</b> It refuses in five directions, and each
/// refusal is the mechanism for one of TASK 14's requirements: it will not create a reference for a release
/// that is not already established in the immutable chain (rule 1), it will not overwrite an existing
/// reference, it will not move one, it will not point one at a commit the release does not carry (rule 2),
/// and it will not write a tag whose target cannot be resolved. A creation that is asked for twice with the
/// same release is a no-op rather than a second write, so a retried release run does not fail for having
/// succeeded.
/// </para>
///
/// <para>
/// <b>Rule 1, and why it is "is registered" rather than "is certified".</b> The C-2 rule reads "the tag is
/// created only for an already-certified ReleaseId". Read literally as <i>lifecycle-certified</i> the rule is
/// circular on this estate, because the certification gate refuses on
/// <see cref="ReleaseRefusalReason.ReleaseRefNotGoverned"/> when the reference is absent — so certification
/// cannot precede the creation of the reference the certification requires. The checkable, non-circular
/// content of the rule is the thing the Owner actually needs: <b>a tag cannot bring a ReleaseId into
/// existence.</b> The ReleaseId must already exist in the immutable registry, and its registered record must
/// digest to the record being tagged, before any reference is written for it. A fabricated tag therefore
/// cannot create a release — it can only point at one that already exists — which is exactly what negative
/// control C-3 asserts.
/// </para>
///
/// <para>
/// <b>The registry and not a boolean.</b> The publisher asks the registry rather than accepting a caller's
/// assertion that the release exists, for the same reason the deployment stage re-verifies the remote rather
/// than trusting a recorded flag: a caller-supplied boolean is a check that can only ever agree with the
/// caller.
/// </para>
/// </summary>
public sealed class GitReleaseTagPublisher : IReleaseTagPublisher
{
    private readonly IProcessRunner _runner;
    private readonly IReleaseTagPolicy _policy;
    private readonly IReleaseRegistry _registry;

    public GitReleaseTagPublisher(IProcessRunner runner, IReleaseTagPolicy policy, IReleaseRegistry registry)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ReleaseTagCreationOutcome> PublishAsync(
        string repositoryLabel,
        string repositoryRoot,
        ReleaseRecord release,
        string tagTargetCommitSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagTargetCommitSha);

        var refName = IReleaseTagPolicy.RefNameFor(release);
        var shortName = IReleaseTagPolicy.ShortNameFor(release.UnitId, release.Version);

        // ---- Rule 1: the ReleaseId must already exist, and be the record being tagged --------------------
        var entry = await _registry
            .TryGetEntryAsync(release.ReleaseId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return ReleaseTagCreationOutcome.Refused(
                refName,
                [ReleaseTagRefusalReason.ReleaseNotEligibleForReference],
                [
                    $"No release '{release.ReleaseId}' is registered, so no reference may be created for it.",
                    "A release reference is published for a ReleaseId that already exists in the immutable chain. A tag never brings a release into existence."
                ]);
        }

        var presentedDigest = release.ComputeRecordDigest();

        if (entry.RecordDigest != presentedDigest)
        {
            return ReleaseTagCreationOutcome.Refused(
                refName,
                [ReleaseTagRefusalReason.ReleaseNotEligibleForReference],
                [
                    $"The registry holds {release.ReleaseId} with record digest {entry.RecordDigest}, but the record presented hashes to {presentedDigest}.",
                    "The reference may only be created for the release as it was registered. Changed content is a NEW release, never a re-pointed reference."
                ]);
        }

        // ---- Rule 2: the tag may only point at a commit the certified build recorded ---------------------
        // Judged by ReleaseRefTargetRule and not by an inline Contains, because the deployment gate judges
        // the same claim about a remote observation. One judgement, one place.
        if (!ReleaseRefTargetRule.PointsAtACommitTheReleaseCarries(release, tagTargetCommitSha))
        {
            return ReleaseTagCreationOutcome.Refused(
                refName,
                [ReleaseTagRefusalReason.TagDoesNotContainBuildCommit],
                [
                    ReleaseRefTargetRule.DescribeRefusal(release, tagTargetCommitSha),
                    "Without this the governed reference could be placed at any commit, and 'the release reference identifies the certified source state' would stop being true."
                ]);
        }

        // Does it already exist? Read before writing, so the answer comes from the repository and not from
        // this process's memory of what it did.
        var existing = await _runner
            .RunAsync("git", ["rev-parse", "--verify", "--quiet", refName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        if (existing.Succeeded)
        {
            var assessment = await _policy
                .AssessRefAsync(repositoryLabel, repositoryRoot, refName, release, cancellationToken)
                .ConfigureAwait(false);

            if (assessment.IsGoverned)
            {
                // Byte-identical governed reference already present. A no-op, not a second write — the same
                // reasoning as the artifact store and the release registry.
                return ReleaseTagCreationOutcome.Created(refName, assessment.Descriptor?.TagObjectSha ?? string.Empty);
            }

            return ReleaseTagCreationOutcome.Refused(
                refName,
                assessment.RefusalReasons.Count > 0
                    ? assessment.RefusalReasons
                    : [ReleaseTagRefusalReason.TagWasMoved],
                [
                    $"'{refName}' already exists in '{repositoryLabel}' and is not this release's governed reference.",
                    "An existing release reference is never overwritten or moved. A changed release produces a new version and therefore a new reference.",
                    .. assessment.Detail
                ]);
        }

        var annotation = ReleaseTagAnnotation.Format(release);

        // An annotated tag, written with -a and a message. The message IS the governed block: this is the
        // only place a release reference acquires its verifiable identity.
        //
        // The SHORT name, because `git tag` resolves its argument relative to refs/tags/ — passing the full
        // ref creates refs/tags/refs/tags/... . This was not inferred: it is the defect the first real run
        // produced, after the suite was green. See IReleaseTagPolicy.ShortNameFor.
        var create = await _runner
            .RunAsync(
                "git",
                ["tag", "-a", shortName, tagTargetCommitSha, "-m", annotation],
                repositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);

        if (!create.Succeeded)
        {
            return ReleaseTagCreationOutcome.Refused(
                refName,
                [ReleaseTagRefusalReason.ReleaseNotEligibleForReference],
                [$"git refused to create '{refName}': {FirstLine(create.StandardError)}"]);
        }

        var tagObject = await _runner.RunAsync("git", ["rev-parse", refName], repositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        return ReleaseTagCreationOutcome.Created(refName, tagObject.Succeeded ? tagObject.StandardOutput.Trim() : string.Empty);
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(no detail)";
        }

        var line = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].Trim();
        return line.Length == 0 ? "(no detail)" : line;
    }
}
