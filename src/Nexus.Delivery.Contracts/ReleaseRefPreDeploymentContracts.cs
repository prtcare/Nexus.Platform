using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// What the <b>remote</b> says at the release reference's name.
///
/// <para>
/// <b>This is an observation, not a judgement.</b> It answers only "what is at that name on the remote
/// right now", and every member is a fact a single <c>git ls-remote</c> establishes. Whether what was found
/// is the release's governed reference — and therefore whether a deployment may proceed — is decided one
/// level up by <see cref="ReleaseRefPreDeploymentState"/>, because that question needs the local governed
/// assessment and the release record and this one does not.
/// </para>
///
/// <para>
/// <b>Absence and unreachability are separate members.</b> They are not flavours of one failure. A remote
/// that answered and has no ref at that name has told us something definite and expected — the tag is not
/// published yet. A remote that could not be reached has told us nothing at all, and collapsing the two
/// would let an unqueryable remote read as "nothing there", which is the shape of every check this estate
/// has recorded as unable-to-fail.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RemoteReleaseTagObservationState
{
    /// <summary>The remote could not be queried. Nothing about the ref is established.</summary>
    Unreachable,

    /// <summary>The remote answered and has no ref at the release reference name.</summary>
    Absent,

    /// <summary>
    /// The remote ref resolves straight to a commit. That is a lightweight tag: it carries no message, so it
    /// cannot carry the governed block, so it cannot be this release's verified pointer whatever its name.
    /// </summary>
    Lightweight,

    /// <summary>An annotated tag: a tag object that was peeled to the commit it points at.</summary>
    Annotated
}

/// <summary>
/// The remote's answer at one release reference name, with the two object ids the mechanism records.
///
/// <para>
/// <b>Two ids, because they answer different questions.</b> <see cref="RemoteTagObjectSha"/> is the identity
/// of the tag object itself — what makes "the remote holds the same reference object the governed publisher
/// wrote" checkable. <see cref="RemoteTargetCommitSha"/> is the commit the tag points at — what makes "the
/// reference still points at the certified source" checkable. A tag can be identical as an object while
/// pointing somewhere else only if its object id changed, because the object id covers the target; the two
/// are recorded separately anyway because they are separately reported and separately refused.
/// </para>
/// </summary>
public sealed record RemoteReleaseTagObservation
{
    private RemoteReleaseTagObservation(
        string remote,
        string refName,
        RemoteReleaseTagObservationState state,
        string? remoteTagObjectSha,
        string? remoteTargetCommitSha,
        IReadOnlyList<string> detail)
    {
        Remote = remote;
        RefName = refName;
        State = state;
        RemoteTagObjectSha = remoteTagObjectSha;
        RemoteTargetCommitSha = remoteTargetCommitSha;
        Detail = detail;
    }

    /// <summary>The remote as the caller named it. Never resolved to a URL here; that is the host's business.</summary>
    public string Remote { get; }

    /// <summary>The full ref name that was asked about, from <see cref="IReleaseTagPolicy.RefNameFor"/>.</summary>
    public string RefName { get; }

    public RemoteReleaseTagObservationState State { get; }

    /// <summary>The tag object's sha, when the remote holds an annotated tag. This is the value rule 9 records.</summary>
    public string? RemoteTagObjectSha { get; }

    /// <summary>The commit the remote ref points at, when it resolves to one.</summary>
    public string? RemoteTargetCommitSha { get; }

    public IReadOnlyList<string> Detail { get; }

    /// <summary>True only when the remote answered <b>and</b> holds an annotated tag at the name.</summary>
    public bool HoldsAnAnnotatedTag => State == RemoteReleaseTagObservationState.Annotated;

    public static RemoteReleaseTagObservation Unreachable(string remote, string refName, string reason)
        => new(remote, refName, RemoteReleaseTagObservationState.Unreachable, null, null,
        [
            $"'{remote}' could not be queried, so nothing is established about '{refName}'.",
            reason
        ]);

    public static RemoteReleaseTagObservation Absent(string remote, string refName)
        => new(remote, refName, RemoteReleaseTagObservationState.Absent, null, null,
        [
            $"'{remote}' holds no ref at '{refName}'.",
            "This is the state before the governed publisher has published the reference. It is not drift: drift is the remote saying something different, and this is the remote saying nothing."
        ]);

    public static RemoteReleaseTagObservation Lightweight(string remote, string refName, string commitSha)
        => new(remote, refName, RemoteReleaseTagObservationState.Lightweight, null, commitSha,
        [
            $"'{remote}' holds a lightweight tag at '{refName}', resolving directly to {commitSha}.",
            "A lightweight tag carries no message, so it cannot carry the governed release identity, so it cannot be this release's verified pointer."
        ]);

    public static RemoteReleaseTagObservation Annotated(string remote, string refName, string tagObjectSha, string targetCommitSha)
        => new(remote, refName, RemoteReleaseTagObservationState.Annotated, tagObjectSha, targetCommitSha,
        [
            $"'{remote}' holds an annotated tag at '{refName}': object {tagObjectSha}, peeling to {targetCommitSha}."
        ]);
}

/// <summary>
/// Reads the remote's answer at a release reference name.
///
/// <para>
/// <b>Read-only, and deliberately unable to create.</b> This interface has one method and it writes to
/// nothing: the reference is published by <see cref="IReleaseTagPublisher"/> and by nothing else. A
/// verifier that could publish the thing it verifies would be able to satisfy its own check, which is the
/// W9.3 assessor/publisher separation carried into the deployment stage.
/// </para>
///
/// <para>
/// <b>It does not re-decide what a governed reference is.</b> "Is this tag a valid governed reference for
/// this release" is answered by <see cref="IReleaseTagPolicy"/> and nowhere else. This type answers the one
/// question the local policy structurally cannot — what is on the remote — so that the two answers can be
/// compared rather than one being re-derived.
/// </para>
/// </summary>
public interface IRemoteReleaseTagVerifier
{
    Task<RemoteReleaseTagObservation> ObserveAsync(
        string remote,
        string repositoryRoot,
        ReleaseRecord release,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The verdict of the pre-deployment release-reference verification: rules 4–9 of the Owner-approved C-2
/// compensating control, decided in one place.
///
/// <para>
/// <b>Why a typed state rather than a boolean.</b> "Do not deploy" is not one condition. A remote that
/// holds a different tag is an attack or a mistake on the remote; a remote that holds nothing is the
/// ordinary pre-publication state; a store whose bytes no longer hash to the certified digest is a
/// corruption finding; and a local reference that is not governed means the release itself is wrong. All
/// four stop a deployment, and a caller that receives <c>false</c> for all four cannot tell an operator
/// which one happened — which is the difference between a control and a red light.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseRefPreDeploymentState
{
    /// <summary>
    /// Every rule held. The local reference is governed, the artifact bytes hash to the certified digest,
    /// and the remote holds that same governed reference at the certified source.
    /// </summary>
    Verified,

    /// <summary>The release's own governed reference could not be established locally. Rules 1–3 did not hold.</summary>
    ReferenceNotGoverned,

    /// <summary>
    /// The stored artifact bytes do not hash to the digest the release records. Rule 6, and the reason rule
    /// 8 exists: the bytes are checked against the chain, never against what the tag says.
    /// </summary>
    ArtifactBytesUnverified,

    /// <summary>
    /// The remote has no ref at the release reference name. <b>Not drift and not success.</b> Deployment is
    /// refused, because rule 4's verification did not happen rather than because it failed.
    /// </summary>
    RemoteReferenceAbsent,

    /// <summary>The remote could not be queried, so no verification happened. Refused for the same reason as absence.</summary>
    RemoteUnreachable,

    /// <summary>
    /// The remote holds a ref that is not this release's governed reference — a different object, a
    /// lightweight tag, or a tag pointing at a commit other than the certified source. The typed refusal is
    /// <c>RELEASE_REFERENCE_DRIFT</c>.
    /// </summary>
    ReferenceDrift
}

/// <summary>
/// The pre-deployment verdict, with the observed remote identity it was reached from.
///
/// <para>
/// <see cref="ObservedRemoteTagObjectSha"/> and <see cref="ObservedRemoteTargetCommitSha"/> are carried
/// even when the verdict is a refusal, because rule 9 requires the <i>observed</i> value to be recorded and
/// an observation that is only recorded on success is not a record of what was seen.
/// </para>
/// </summary>
public sealed record ReleaseRefPreDeploymentVerdict
{
    /// <summary>
    /// The verdict value the Owner's deviation names, used verbatim so that a deployment record and the
    /// approved control can be read against each other without translation.
    /// </summary>
    public const string ReleaseReferenceDriftVerdict = "RELEASE_REFERENCE_DRIFT";

    /// <summary>The verdict value when the verification ran and everything agreed.</summary>
    public const string VerifiedVerdict = "RELEASE_REFERENCE_VERIFIED";

    /// <summary>The verdict value when the remote holds no ref at the release reference name.</summary>
    public const string RemoteReferenceAbsentVerdict = "RELEASE_REFERENCE_ABSENT_REMOTELY";

    private ReleaseRefPreDeploymentVerdict(
        ReleaseRefPreDeploymentState state,
        string verdictText,
        string remote,
        string refName,
        IReadOnlyList<string> expectedTargetCommitShas,
        string? expectedTagObjectSha,
        string? observedRemoteTagObjectSha,
        string? observedRemoteTargetCommitSha,
        IReadOnlyList<ReleaseRefusalReason> refusalReasons,
        IReadOnlyList<string> detail)
    {
        State = state;
        VerdictText = verdictText;
        Remote = remote;
        RefName = refName;
        ExpectedTargetCommitShas = expectedTargetCommitShas;
        ExpectedTagObjectSha = expectedTagObjectSha;
        ObservedRemoteTagObjectSha = observedRemoteTagObjectSha;
        ObservedRemoteTargetCommitSha = observedRemoteTargetCommitSha;
        RefusalReasons = refusalReasons;
        Detail = detail;
    }

    public ReleaseRefPreDeploymentState State { get; }

    /// <summary>The machine-readable verdict: one of the <c>*Verdict</c> constants on this type.</summary>
    public string VerdictText { get; }

    public string Remote { get; }

    public string RefName { get; }

    /// <summary>
    /// The certified source commits the remote reference may point at (rule 5).
    ///
    /// <para>
    /// <b>A set, because the rule is membership.</b> A release's identity carries the source commits of every
    /// repository its build read, while a release reference lives in exactly one repository, so the checkable
    /// claim is "the reference points at a commit this release was built from". Reporting a single expected
    /// commit here would make the recorded evidence narrower than the rule actually applied — and evidence
    /// that understates what was checked is the same defect as evidence that overstates it.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ExpectedTargetCommitShas { get; }

    /// <summary>The governed tag object the local reference occupies, when it could be read.</summary>
    public string? ExpectedTagObjectSha { get; }

    /// <summary>Rule 9: the remote tag object sha as observed. Recorded whether or not it agreed.</summary>
    public string? ObservedRemoteTagObjectSha { get; }

    /// <summary>The commit the remote reference was observed to point at.</summary>
    public string? ObservedRemoteTargetCommitSha { get; }

    public IReadOnlyList<ReleaseRefusalReason> RefusalReasons { get; }

    public IReadOnlyList<string> Detail { get; }

    public bool IsDeployable => State == ReleaseRefPreDeploymentState.Verified;

    public bool RefusedBecause(ReleaseRefusalReason reason) => RefusalReasons.Contains(reason);

    /// <summary>True when the refusal is the Owner-named drift condition rather than absence or unverifiability.</summary>
    public bool IsReferenceDrift => State == ReleaseRefPreDeploymentState.ReferenceDrift;

    public static ReleaseRefPreDeploymentVerdict Verified(
        string remote,
        string refName,
        IReadOnlyList<string> expectedTargetCommitShas,
        string? expectedTagObjectSha,
        string observedRemoteTagObjectSha,
        string observedRemoteTargetCommitSha,
        IReadOnlyList<string> detail)
        => new(
            ReleaseRefPreDeploymentState.Verified,
            VerifiedVerdict,
            remote,
            refName,
            expectedTargetCommitShas,
            expectedTagObjectSha,
            observedRemoteTagObjectSha,
            observedRemoteTargetCommitSha,
            [],
            detail);

    public static ReleaseRefPreDeploymentVerdict Refused(
        ReleaseRefPreDeploymentState state,
        string remote,
        string refName,
        IReadOnlyList<string> expectedTargetCommitShas,
        string? expectedTagObjectSha,
        string? observedRemoteTagObjectSha,
        string? observedRemoteTargetCommitSha,
        IReadOnlyList<ReleaseRefusalReason> reasons,
        IReadOnlyList<string> detail)
    {
        if (state == ReleaseRefPreDeploymentState.Verified)
        {
            throw new ArgumentException("A refusal cannot carry the verified state.", nameof(state));
        }

        if (reasons is null || reasons.Count == 0)
        {
            throw new ArgumentException("A refused pre-deployment verdict must name at least one typed reason.", nameof(reasons));
        }

        var verdict = state switch
        {
            ReleaseRefPreDeploymentState.ReferenceDrift => ReleaseReferenceDriftVerdict,
            ReleaseRefPreDeploymentState.RemoteReferenceAbsent => RemoteReferenceAbsentVerdict,
            _ => state.ToString()
        };

        return new ReleaseRefPreDeploymentVerdict(
            state,
            verdict,
            remote,
            refName,
            expectedTargetCommitShas,
            expectedTagObjectSha,
            observedRemoteTagObjectSha,
            observedRemoteTargetCommitSha,
            [.. reasons.Distinct().OrderBy(r => r)],
            detail ?? []);
    }
}
