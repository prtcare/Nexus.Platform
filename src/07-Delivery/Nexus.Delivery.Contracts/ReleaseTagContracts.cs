using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Why a candidate release tag is not a governed release reference.
///
/// <para>
/// <b>Why a separate vocabulary from <see cref="ReleaseRefRefusalReason"/>.</b> That enum answers "does a
/// governed <i>branch</i> exist containing the build commit", which is the question the build stage asks
/// about its input. This answers "is this <i>annotated tag</i> a valid release reference for this release",
/// which is the question the release stage asks about its output. They refuse for different reasons — a
/// lightweight tag and a branch that does not contain a commit are not variations of one problem — and
/// merging them would produce a reader that cannot tell which mechanism failed.
/// </para>
///
/// <para>
/// The protection judgement itself is <b>not</b> duplicated: both use <see cref="ReleaseRefProtectionStatus"/>,
/// because "protected" means one thing in this estate and must not acquire a second definition.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseTagRefusalReason
{
    None = 0,

    /// <summary>No tag exists at the release reference name.</summary>
    TagAbsent,

    /// <summary>
    /// The ref is a lightweight tag. <b>A lightweight tag is a pointer and nothing else</b> — it carries no
    /// message, so it cannot record which release it names, and therefore cannot be verified against the
    /// release it claims to identify. It is refused rather than upgraded, because upgrading it would mean
    /// writing the governed annotation after the fact, which is exactly the fabrication this refuses.
    /// </summary>
    LightweightTagNotPermitted,

    /// <summary>The ref is not in the governed release-tag namespace.</summary>
    OutsideReleaseTagNamespace,

    /// <summary>
    /// A branch head was offered as a release reference. A branch moves; an immutable release reference
    /// cannot. This is refused explicitly because the substitution is the obvious shortcut.
    /// </summary>
    BranchCannotSubstituteForImmutableTag,

    /// <summary>An annotated tag exists but its message carries no governed release block.</summary>
    TagAnnotationNotGoverned,

    /// <summary>An annotated tag exists and carries a governed block, but the block names a different release.</summary>
    TagIdentifiesDifferentRelease,

    /// <summary>The tag does not contain the commit the certified build recorded.</summary>
    TagDoesNotContainBuildCommit,

    /// <summary>An existing tag was moved: its recorded release identity no longer matches the release being assessed.</summary>
    TagWasMoved,

    /// <summary>Server-side protection could not be verified from this stage, and is not assumed (the Owner's D2 requirement).</summary>
    ServerSideProtectionUnverified,

    /// <summary>The release itself is not in a state from which a reference may be created.</summary>
    ReleaseNotEligibleForReference,

    /// <summary>
    /// The remote already holds a <b>different</b> object at the release reference name.
    ///
    /// <para>
    /// <b>Refused, never corrected.</b> This is the one case where the tempting remedy is a force-push, and a
    /// force-push is precisely what the reference's immutability rules out: an immutable reference that can be
    /// re-pointed is not immutable. A remote that holds something else at this name is either a genuine drift
    /// event — somebody moved the reference, which is a security finding — or an unrelated tag occupying the
    /// governed name, which is refused as drift for the same reason. C-2's control returns
    /// <c>RELEASE_REFERENCE_DRIFT</c> for the same situation at deployment time; this is the same judgement
    /// made before the reference is published rather than after.
    /// </para>
    /// </summary>
    RemoteRefHoldsADifferentObject,

    /// <summary>The remote could not be queried, so nothing about the reference is established — not even its absence.</summary>
    RemoteUnreachable
}

/// <summary>What the governed remote publication did, or why it did nothing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseTagRemotePublicationState
{
    /// <summary>The governed reference was pushed, and the remote was re-observed holding it.</summary>
    Published = 0,

    /// <summary>The remote already held the identical governed reference object. Nothing was written.</summary>
    AlreadyPublished,

    /// <summary>Nothing was published, and nothing on the remote was touched.</summary>
    Refused
}

/// <summary>
/// The result of publishing a release reference to a remote, <b>with the remote's own answer afterwards</b>.
///
/// <para>
/// <b>The observed ids are the point of this record.</b> A push command that exits zero says the local side
/// believed it sent something; C-2 rule 9 requires the <i>observed</i> remote tag sha to be recorded, which
/// means reading the remote back after the push rather than trusting the push's exit code. Both ids are
/// carried for the reason <see cref="RemoteReleaseTagObservation"/> carries both: the tag object identifies
/// the reference, and the peeled commit says what it points at, and a reference that is the right object
/// while pointing elsewhere is not a thing that can happen — which is exactly why it is checked.
/// </para>
/// </summary>
public sealed record ReleaseTagRemotePublicationOutcome(
    bool IsPublished,
    ReleaseTagRemotePublicationState State,
    string RefName,
    string Remote,
    string? LocalTagObjectSha,
    string? ObservedRemoteTagObjectSha,
    string? ObservedRemoteTargetCommitSha,
    IReadOnlyList<ReleaseTagRefusalReason> RefusalReasons,
    IReadOnlyList<string> Detail)
{
    /// <summary>True when the remote holds this release's governed reference, whether or not this call pushed it.</summary>
    public bool RemoteHoldsTheGovernedReference => State is ReleaseTagRemotePublicationState.Published
        or ReleaseTagRemotePublicationState.AlreadyPublished;

    public bool RefusedBecause(ReleaseTagRefusalReason reason) => RefusalReasons.Contains(reason);

    /// <param name="observedRemoteTagObjectSha">
    /// The remote's own answer, read back after the write. Nullable because a remote observation is not always
    /// able to report both ids, and a record that forced one would have to invent it.
    /// </param>
    public static ReleaseTagRemotePublicationOutcome Published(
        string refName,
        string remote,
        string localTagObjectSha,
        string? observedRemoteTagObjectSha,
        string? observedRemoteTargetCommitSha,
        bool alreadyPresent,
        IReadOnlyList<string> detail)
        => new(
            true,
            alreadyPresent ? ReleaseTagRemotePublicationState.AlreadyPublished : ReleaseTagRemotePublicationState.Published,
            refName,
            remote,
            localTagObjectSha,
            observedRemoteTagObjectSha,
            observedRemoteTargetCommitSha,
            [],
            detail);

    public static ReleaseTagRemotePublicationOutcome Refused(
        string refName,
        string remote,
        string? localTagObjectSha,
        string? observedRemoteTagObjectSha,
        string? observedRemoteTargetCommitSha,
        IReadOnlyList<ReleaseTagRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("A refused publication must name at least one typed reason.", nameof(reasons));
        }

        return new(
            false,
            ReleaseTagRemotePublicationState.Refused,
            refName,
            remote,
            localTagObjectSha,
            observedRemoteTagObjectSha,
            observedRemoteTargetCommitSha,
            [.. reasons.Distinct().OrderBy(r => r)],
            detail ?? []);
    }
}

/// <summary>
/// A release tag as observed, with the governed identity its annotation carries.
///
/// <para>
/// <see cref="AnnotatedReleaseId"/> and <see cref="AnnotatedBuildId"/> are read <b>from the tag's own
/// message</b>, not from the release being assessed. That is what makes the two comparable: a tag that names
/// a different release is not this release's reference, and a tag whose message was rewritten during a move
/// names a different release than the one its ref points at.
/// </para>
/// </summary>
public sealed record ReleaseTagDescriptor(
    string RepositoryLabel,
    string RefName,
    bool Exists,
    bool IsAnnotated,
    ReleaseRefProtectionStatus ProtectionStatus,
    string? TagObjectSha = null,
    string? PeeledCommitSha = null,
    ReleaseId? AnnotatedReleaseId = null,
    BuildId? AnnotatedBuildId = null,
    ArtifactDigest? AnnotatedRecordDigest = null,
    string? ProtectionMechanism = null);

/// <summary>The outcome of assessing whether a release tag is a governed release reference for a release.</summary>
public sealed record ReleaseTagAssessment
{
    private ReleaseTagAssessment(
        bool isEligible,
        ReleaseTagDescriptor? descriptor,
        IReadOnlyList<ReleaseTagRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        IsEligible = isEligible;
        Descriptor = descriptor;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public bool IsEligible { get; }

    public ReleaseTagDescriptor? Descriptor { get; }

    public IReadOnlyList<ReleaseTagRefusalReason> RefusalReasons { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool RefusedBecause(ReleaseTagRefusalReason reason) => RefusalReasons.Contains(reason);

    /// <summary>
    /// True when the tag is a valid governed reference <b>except</b> that the remote's server-side ruleset
    /// could not be observed. This is the local-proof case the Owner's D2 ruling anticipates: the mechanism
    /// is in place locally, the authoritative control is not yet installed, and the distinction is reported
    /// rather than collapsed.
    /// </summary>
    public bool IsGovernedLocallyButUnprotectedRemotely
        => RefusalReasons.Count > 0
           && RefusalReasons.All(r => r == ReleaseTagRefusalReason.ServerSideProtectionUnverified);

    /// <summary>
    /// True when the ref <b>is</b> a valid governed release reference for this release, whether or not the
    /// remote's ruleset was observable. Server-side protection is deliberately excluded from this judgement:
    /// it is an orthogonal fact about the world, and folding it in would make "the reference is governed"
    /// and "the remote protects it" the same question — which is exactly the conflation the Owner's D2
    /// ruling separates.
    /// </summary>
    public bool IsGoverned
        => RefusalReasons.All(r => r == ReleaseTagRefusalReason.ServerSideProtectionUnverified);

    public static ReleaseTagAssessment Eligible(ReleaseTagDescriptor descriptor)
        => new(true, descriptor, [], [$"Governed release reference: {descriptor.RepositoryLabel}:{descriptor.RefName}"]);

    public static ReleaseTagAssessment Ineligible(
        ReleaseTagDescriptor? descriptor,
        IReadOnlyList<ReleaseTagRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("An ineligible assessment must name at least one typed reason.", nameof(reasons));
        }

        return new ReleaseTagAssessment(false, descriptor, [.. reasons.Distinct().OrderBy(r => r)], detail ?? []);
    }
}

/// <summary>The outcome of creating a release tag.</summary>
public sealed record ReleaseTagCreationOutcome
{
    private ReleaseTagCreationOutcome(
        bool isCreated,
        string refName,
        string? tagObjectSha,
        IReadOnlyList<ReleaseTagRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        IsCreated = isCreated;
        RefName = refName;
        TagObjectSha = tagObjectSha;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public bool IsCreated { get; }

    public string RefName { get; }

    public string? TagObjectSha { get; }

    public IReadOnlyList<ReleaseTagRefusalReason> RefusalReasons { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool Created(ReleaseTagRefusalReason reason) => RefusalReasons.Contains(reason);

    public static ReleaseTagCreationOutcome Created(string refName, string tagObjectSha)
        => new(true, refName, tagObjectSha, [], [$"Created {refName} → {tagObjectSha}"]);

    public static ReleaseTagCreationOutcome Refused(
        string refName,
        IReadOnlyList<ReleaseTagRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("A refused creation must name at least one typed reason.", nameof(reasons));
        }

        return new ReleaseTagCreationOutcome(false, refName, null, [.. reasons.Distinct().OrderBy(r => r)], detail ?? []);
    }
}

/// <summary>
/// Assesses whether a release tag is a governed release reference for a given release.
///
/// <para>
/// <b>Assessment is not creation.</b> This interface only reads. Creating a release reference is a governed
/// act with preconditions and is owned by <see cref="IReleaseTagPublisher"/>; keeping the two apart is what
/// lets an assessor refuse a tag it did not create without also being able to make one.
/// </para>
/// </summary>
public interface IReleaseTagPolicy
{
    /// <summary>Assesses the reference the release is entitled to occupy.</summary>
    Task<ReleaseTagAssessment> AssessAsync(
        string repositoryLabel,
        string repositoryRoot,
        ReleaseRecord release,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assesses an arbitrary candidate ref against a release.
    ///
    /// <para>
    /// This exists so that the refusals are checkable rather than merely asserted. "A branch cannot
    /// substitute for an immutable release reference" is only a control if something can be pointed at a
    /// branch and refuse it; otherwise it is a sentence in a document. Same for a lightweight tag, a tag
    /// whose annotation names a different release, and a tag that was moved.
    /// </para>
    /// </summary>
    Task<ReleaseTagAssessment> AssessRefAsync(
        string repositoryLabel,
        string repositoryRoot,
        string refName,
        ReleaseRecord release,
        CancellationToken cancellationToken = default);

    /// <summary>The ref name a release's reference must occupy, per the Owner's D-W9.3-01 ruling.</summary>
    static string RefNameFor(ReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return RefNameFor(release.UnitId, release.Version);
    }

    /// <summary>
    /// <c>release/&lt;DeploymentUnitId&gt;/&lt;Version&gt;</c> in the tag namespace. The logical format is the
    /// Owner's; the <c>refs/tags/</c> prefix is git's, and it is what makes the reference immutable rather
    /// than a moving name.
    /// </summary>
    static string RefNameFor(DeploymentUnitId unitId, string version)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        if (!ArtifactId.IsValidVersion(version))
        {
            throw new ArgumentException($"Not a valid release version: '{version}'.", nameof(version));
        }

        return $"refs/tags/{ShortNameFor(unitId, version)}";
    }

    /// <summary>
    /// The <b>short</b> tag name — <c>release/&lt;DeploymentUnitId&gt;/&lt;Version&gt;</c> — which is what
    /// <c>git tag</c> takes as its argument.
    ///
    /// <para>
    /// <b>Why this is a separate member and not a prefix stripped at the call site.</b> <c>git tag</c>
    /// resolves its name argument relative to <c>refs/tags/</c>: given <c>release/x/1.0.0</c> it creates
    /// <c>refs/tags/release/x/1.0.0</c> — and given a full <c>refs/tags/release/x/1.0.0</c> it creates
    /// <c>refs/tags/refs/tags/release/x/1.0.0</c>. That is not a guess; it is what this mechanism did on
    /// its first real run against a repository, having passed its tests, because the tests shared the
    /// assumption rather than checking it. Reading a ref and writing a tag take genuinely different names,
    /// so both names are declared here, one line apart, where a reader can see they differ.
    /// </para>
    /// </summary>
    static string ShortNameFor(DeploymentUnitId unitId, string version)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        if (!ArtifactId.IsValidVersion(version))
        {
            throw new ArgumentException($"Not a valid release version: '{version}'.", nameof(version));
        }

        return $"release/{unitId.Value}/{version}";
    }
}

/// <summary>
/// Creates the immutable release reference.
///
/// <para>
/// <b>Only an annotated tag.</b> The annotation is not decoration: it is where the governed identity block
/// is recorded, and it is the only reason a later assessor can tell a governed release reference from a
/// tag someone made with the right name. A lightweight tag has no message, so it cannot carry that, so it
/// cannot be governed.
/// </para>
///
/// <para>
/// <b>Creation never moves an existing tag.</b> A release reference that already exists at a different
/// source is refused, not repointed. Git would let the tag be deleted and recreated; this will not, because
/// an immutable reference that can be re-pointed is not immutable and the whole build-once argument rests on
/// it being so.
/// </para>
/// </summary>
public interface IReleaseTagPublisher
{
    /// <param name="tagTargetCommitSha">
    /// The commit the tag points at. Supplied rather than derived: a release's identity carries the commits
    /// of <i>every</i> repository its build read, and a tag lives in one. Choosing one of several silently
    /// would place the governed reference at a commit nobody selected, so the caller names it — and an
    /// implementation refuses a commit the release does not carry.
    /// </param>
    Task<ReleaseTagCreationOutcome> PublishAsync(
        string repositoryLabel,
        string repositoryRoot,
        ReleaseRecord release,
        string tagTargetCommitSha,
        CancellationToken cancellationToken = default);
}
