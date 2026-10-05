using System.Text;
using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Whether the W9.0 credential exposure has been rotated or revoked, as the Human Owner records it.
///
/// <para>
/// <b>A three-member state, not a boolean.</b> The member this replaces was
/// <c>credentialRotationConfirmed: bool</c> in a hand-edited, untracked JSON file, where <c>false</c> had to
/// carry two different meanings: "nobody has looked" and "somebody looked and the rotation has not been
/// done". Those route differently — the first is an outstanding observation, the second is an outstanding
/// <i>action</i> — and a release waiting on a human act is a different thing from a release nobody has
/// assessed. W9.4's own re-check had to read the raw file to tell the two apart, which is the defect this
/// member removes.
/// </para>
///
/// <para>
/// <b>Never established by reading the credential.</b> <c>CONFIGURATION_STANDARDS.md</c> §13.2 requires
/// rotation first and does not branch on whether the value is still live. This is a statement about a
/// recorded human decision, and the estate records it as one.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialRotationStatus
{
    /// <summary>Nothing is recorded. The default, and never a pass.</summary>
    Unknown = 0,

    /// <summary>The exposure is recorded as open: the rotation or revocation is required and has not been reported done.</summary>
    Required,

    /// <summary>The Human Owner has recorded the rotation or revocation as complete, and named the decision that says so.</summary>
    Confirmed
}

/// <summary>
/// Why a Release Plan record may not be written, or may not be used to release.
///
/// <para>
/// <b>Refusals, not exceptions.</b> The writer returns these; only a genuinely exceptional condition (I/O
/// failure, a corrupt record) escapes as an exception, and even that is reported as a typed
/// <see cref="ReleasePlanWriteRefusalReason.StorageFailure"/> where it can be caught. Same discipline as
/// <see cref="ReleaseRegistryRefusalReason"/> one level down: a caller must be able to record <i>why</i> a
/// write was refused without parsing prose.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleasePlanRefusalReason
{
    None = 0,

    /// <summary>The plan names no release, so nothing can look it up.</summary>
    ReleaseIdMissing,

    /// <summary>The record's schema version is outside the range this contract understands.</summary>
    SchemaVersionUnsupported,

    /// <summary>A plan is versioned from 1; a version below that is not a version.</summary>
    PlanVersionNotPositive,

    /// <summary>No actor is recorded. An unattributed change is one nobody can be asked about.</summary>
    ProvenanceMissing,

    /// <summary>No reason is recorded. "Withdrawn" and "vanished" look identical without one.</summary>
    UpdateReasonMissing,

    /// <summary>No timestamp is recorded, so the record cannot be placed relative to anything.</summary>
    UpdateTimestampMissing,

    // ---- C-1 -------------------------------------------------------------------------------------------

    /// <summary>
    /// Rotation is recorded <see cref="CredentialRotationStatus.Confirmed"/> with no decision named.
    /// A confirmation with no decision behind it is indistinguishable from an assertion.
    /// </summary>
    RotationConfirmationNotRecorded,

    /// <summary>
    /// Rotation is recorded <see cref="CredentialRotationStatus.Confirmed"/> by a party other than the
    /// Human Owner. Rotation is an Owner act; the role the account holds does not transfer it.
    /// </summary>
    RotationConfirmationNotOwnerAuthorized,

    /// <summary>
    /// A rotation decision is named while the status says nothing was confirmed — a decision recorded
    /// against a state it does not describe.
    /// </summary>
    RotationDecisionReferenceOnUnconfirmedState,

    // ---- C-2 -------------------------------------------------------------------------------------------

    /// <summary>No protection mechanism is recorded, so the release reference's protection is unestablished.</summary>
    NoProtectionMechanismRecorded,

    /// <summary>The compensating control is recorded as not approved. The Owner's approval is the whole mechanism.</summary>
    CompensatingControlNotApproved,

    /// <summary>The compensating control is approved but its rules were never verified against this release's reference.</summary>
    CompensatingControlNotVerified,

    /// <summary>The compensating control is recorded with no environment scope, so nothing says where it applies.</summary>
    CompensatingControlScopeMissing,

    /// <summary>
    /// The compensating control claims <see cref="ReleaseReferenceProtectionScope.AllRatifiedEnvironments"/>.
    /// Refused rather than narrowed to DEV/TEST: silently treating it as DEV/TEST would leave the record
    /// saying one thing and the behaviour doing another.
    /// </summary>
    CompensatingControlScopeExceedsDevTest,

    /// <summary>
    /// The compensating control is recorded while server-side protection is claimed as verified. They are
    /// different mechanisms and one plan may rely on one of them; claiming both is how a substitute starts
    /// reporting itself as the control it replaced.
    /// </summary>
    CompensatingControlClaimsServerSideProtection,

    /// <summary>
    /// Server-side protection is recorded as the mechanism without an authorized check having observed a
    /// ruleset. This is the exact overstatement the C-2 deviation exists to avoid.
    /// </summary>
    ServerSideProtectionNotVerified,

    /// <summary>
    /// Server-side protection is scoped to DEV/TEST. A ruleset is a property of the repository and applies
    /// wherever the reference is deployed; scoping it to two environments misdescribes it.
    /// </summary>
    ServerSideProtectionCannotBeScopedToDevTest,

    /// <summary>
    /// A mechanism that claims server-side protection has no Owner decision behind it, or one recorded by a
    /// party other than the Owner.
    /// </summary>
    ProtectionMechanismNotOwnerAuthorized,

    /// <summary>An environment scope is recorded while no mechanism is recorded to scope.</summary>
    ScopeRecordedWithoutAMechanism
}

/// <summary>
/// <b>The authoritative, governed state of one release's security decisions.</b> C-1 (credential rotation)
/// and C-2 (release-reference protection) as typed data, written only through the governed writer.
///
/// <para>
/// <b>Why this type exists.</b> Before W9.4, both facts lived as members of a single untracked,
/// hand-edited JSON file (<c>FIRST_RELEASE_PLAN.json</c>) with no writer, no generator, no schema, no
/// validation and no review path. The consumer failed closed on absence, which is why the gap had not
/// caused an incident — but a file that any process can rewrite with a text editor, that git does not
/// track, and that nothing validates is not an authority controlling deployment. The Owner ruled that
/// unacceptable; this is the minimum that closes it.
/// </para>
///
/// <para>
/// <b>Not a general configuration store.</b> Every member here is a fact about <i>one release's</i>
/// security decisions. It is deliberately not a key/value bag, it carries no environment endpoints, no
/// secret references, and nothing consumes it except the release and deployment gates. W9.0's
/// <c>CONFIGURATION_STANDARDS.md</c> governs configuration values; this is a governance record and is
/// scoped to that single job.
/// </para>
///
/// <para>
/// <b>The protection mechanism is reused, not redefined.</b> <see cref="ReleaseReferenceProtectionMode"/>
/// and <see cref="ReleaseReferenceProtectionScope"/> already exist and already carry the one judgement
/// (<see cref="ReleaseReferenceProtectionEvidence.Judge"/>) that both the certification gate and the
/// deployment gate consult. This record composes that model rather than restating it: a second definition
/// of "which mechanism protects the reference" is precisely how the two gates would come to disagree.
/// </para>
/// </summary>
public sealed record ReleaseSecurityPlan
{
    /// <summary>The only schema this contract writes. A record outside the range is refused, not guessed at.</summary>
    public const int CurrentSchemaVersion = 1;

    public const int MinSchemaVersion = 1;

    public const int MaxSchemaVersion = 1;

    /// <summary>
    /// The marker field of the canonical projection. Named, versioned and explicit, exactly as
    /// <c>ReleaseDigestContract</c> does for a release digest: a digest over an unstated projection can
    /// change meaning without changing name.
    /// </summary>
    public const string CanonicalMarker = "nexus-release-security-plan";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The release this plan governs. The lookup key, and the only release it may ever describe.</summary>
    public ReleaseId? ReleaseId { get; init; }

    // ---- C-1 — credential rotation --------------------------------------------------------------------

    public CredentialRotationStatus CredentialRotationStatus { get; init; } = CredentialRotationStatus.Unknown;

    /// <summary>
    /// The recorded decision establishing <see cref="CredentialRotationStatus"/>. Required when the status is
    /// <see cref="CredentialRotationStatus.Confirmed"/>. Operator-facing text only — <b>never</b> a credential
    /// value, and never a hash of one: a hash of a credential is a credential oracle.
    /// </summary>
    public string? CredentialRotationDecisionReference { get; init; }

    // ---- C-2 — release-reference protection ------------------------------------------------------------

    /// <summary>Which mechanism protects the release-tag namespace. Absent means <see cref="ReleaseReferenceProtectionMode.None"/>.</summary>
    public ReleaseReferenceProtectionMode ReleaseReferenceProtectionMode { get; init; }
        = ReleaseReferenceProtectionMode.None;

    /// <summary>
    /// Whether an <b>authorized check observed the remote ruleset</b>. <c>false</c> for this estate and
    /// expected to stay so: no ruleset can be installed on these repositories.
    ///
    /// <para>
    /// <b>This member is not where the C-2 compensating control is recorded.</b> The control is a substitute
    /// for this mechanism; recording it here would assert that a ruleset had been observed. That is the
    /// overstatement the deviation exists to prevent, and validation refuses the pair.
    /// </para>
    /// </summary>
    public bool ReleaseRefServerSideProtectionVerified { get; init; }

    /// <summary>Whether the Human Owner approved the C-2 compensating control for this release.</summary>
    public bool CompensatingControlApproved { get; init; }

    /// <summary>Whether the compensating control's rules were verified against <i>this released</i> reference.</summary>
    public bool CompensatingControlVerified { get; init; }

    /// <summary>
    /// The environments the recorded mechanism may cover. Named <c>AllowedEnvironmentScope</c> because that
    /// is the question a gate asks; the type is the existing
    /// <see cref="ReleaseReferenceProtectionScope"/>, so there is exactly one scope vocabulary in the estate.
    /// </summary>
    public ReleaseReferenceProtectionScope AllowedEnvironmentScope { get; init; }
        = ReleaseReferenceProtectionScope.None;

    // ---- Provenance ------------------------------------------------------------------------------------

    /// <summary>
    /// The Owner decision this plan state rests on — the C-2 ruling for <c>GovernedCompensatingControl</c>,
    /// and the plan's own authorising decision otherwise. Operator-facing text only.
    /// </summary>
    public string? HumanDecisionReference { get; init; }

    /// <summary>
    /// The role that made <see cref="HumanDecisionReference"/>. Defaults to the <i>lowest</i> authority, so an
    /// unset role is refused rather than accepted: a default of <see cref="DeploymentAuthorityRole.Owner"/>
    /// would make every unattributed record an Owner ruling.
    /// </summary>
    public DeploymentAuthorityRole? HumanDecisionAuthority { get; init; }

    /// <summary>When this version was written, from the caller. A clock read inside the store would make the digest unreproducible.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Who wrote this version. Required; an unattributed change is one nobody can be asked about.</summary>
    public string UpdatedBy { get; init; } = string.Empty;

    /// <summary>Why this version was written. Required, for the same reason a lifecycle change requires one.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Monotonic within a release. Starts at 1 and is never reused; the store enforces both.</summary>
    public int Version { get; init; }

    /// <summary>An empty plan for a release: everything unrecorded, which is the fail-closed starting point.</summary>
    public static ReleaseSecurityPlan UnrecordedFor(ReleaseId releaseId)
        => new() { ReleaseId = releaseId };

    /// <summary>
    /// Whether C-1 is satisfied. <b>Only</b> <see cref="CredentialRotationStatus.Confirmed"/> counts;
    /// <see cref="CredentialRotationStatus.Required"/> and <see cref="CredentialRotationStatus.Unknown"/> both
    /// leave the action outstanding, and are reported separately by the assessment.
    /// </summary>
    public bool IsCredentialRotationSatisfied => CredentialRotationStatus == CredentialRotationStatus.Confirmed;

    /// <summary>
    /// The release-reference protection this plan records, in the shape the two gates already judge.
    ///
    /// <para>
    /// <b>The bridge between the governed plan and the gate, and the only one.</b> It is a pure projection:
    /// the gates keep their single judgement, and this record cannot describe protection the gates would
    /// judge differently, because there is nothing here for it to describe it with.
    /// </para>
    /// </summary>
    public ReleaseReferenceProtectionEvidence ToProtectionEvidence() => new()
    {
        Mode = ReleaseReferenceProtectionMode,
        ServerSideProtectionVerified = ReleaseRefServerSideProtectionVerified,
        CompensatingControlApproved = CompensatingControlApproved,
        CompensatingControlVerified = CompensatingControlVerified,
        AllowedScope = AllowedEnvironmentScope
    };

    /// <summary>
    /// The canonical projection a plan digest is taken over, in a stated field order with NUL-terminated
    /// fields — so <c>["ab","c"]</c> and <c>["a","bc"]</c> cannot produce the same bytes.
    ///
    /// <para>
    /// <b>Enums project as names, not numbers.</b> A stored enum ordinal is a value nobody can read and that
    /// a reordered enum silently changes.
    /// </para>
    /// </summary>
    public string CanonicalForm()
    {
        var builder = new StringBuilder();

        void Field(string? value) => builder.Append(value ?? string.Empty).Append('\0');

        Field(CanonicalMarker);
        Field(SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field(ReleaseId?.Value);
        Field(CredentialRotationStatus.ToString());
        Field(CredentialRotationDecisionReference);
        Field(ReleaseReferenceProtectionMode.ToString());
        Field(ReleaseRefServerSideProtectionVerified ? "true" : "false");
        Field(CompensatingControlApproved ? "true" : "false");
        Field(CompensatingControlVerified ? "true" : "false");
        Field(AllowedEnvironmentScope.ToString());
        Field(HumanDecisionReference);
        Field(HumanDecisionAuthority?.ToString());
        Field(UpdatedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        Field(UpdatedBy);
        Field(Reason);
        Field(Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    /// <summary>
    /// The plan's content digest. <b>Not</b> a digest of how the plan happens to be stored: a digest over a
    /// storage rendering is the defect W9.3's <c>D-2</c> recorded, where the registry enforced one digest and
    /// the tag annotation recorded another.
    /// </summary>
    public ArtifactDigest ComputePlanDigest()
        => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalForm()));
}

/// <summary>
/// The answer to "may this plan record be written?", with every rule that failed named.
/// </summary>
/// <param name="IsValid">True only when every well-formedness and coherence rule passed.</param>
public sealed record ReleaseSecurityPlanVerdict(
    bool IsValid,
    IReadOnlyList<ReleasePlanRefusalReason> RefusalReasons,
    IReadOnlyList<string> Detail)
{
    public static ReleaseSecurityPlanVerdict Valid(IReadOnlyList<string> detail)
        => new(true, [], detail);

    public static ReleaseSecurityPlanVerdict Refused(
        IReadOnlyList<ReleasePlanRefusalReason> reasons,
        IReadOnlyList<string> detail)
        => new(false, [.. reasons.Distinct().OrderBy(r => r)], detail);

    public bool RefusedBecause(ReleasePlanRefusalReason reason) => RefusalReasons.Contains(reason);
}

/// <summary>
/// The answer to the release-security question a gate asks: <b>may this release proceed into this
/// environment</b> on the strength of what the governed plan records?
///
/// <para>
/// <b>Separate from <see cref="ReleaseSecurityPlanVerdict"/>, and deliberately so.</b> Well-formedness is
/// enforced on every write; readiness is a judgement about a plan that is already well formed. A plan
/// recording <see cref="CredentialRotationStatus.Required"/> and
/// <see cref="ReleaseReferenceProtectionMode.None"/> is a perfectly valid record of an unfinished
/// situation — refusing to <i>store</i> it would push the estate back to hand-editing, which is the defect
/// being closed.
/// </para>
/// </summary>
public sealed record ReleaseSecurityReadiness(
    bool IsReady,
    DeploymentEnvironmentId Environment,
    IReadOnlyList<ReleasePlanRefusalReason> RefusalReasons,
    IReadOnlyList<string> Detail)
{
    public static ReleaseSecurityReadiness Ready(DeploymentEnvironmentId environment, IReadOnlyList<string> detail)
        => new(true, environment, [], detail);

    public static ReleaseSecurityReadiness NotReady(
        DeploymentEnvironmentId environment,
        IReadOnlyList<ReleasePlanRefusalReason> reasons,
        IReadOnlyList<string> detail)
        => new(false, environment, [.. reasons.Distinct().OrderBy(r => r)], detail);

    public bool RefusedBecause(ReleasePlanRefusalReason reason) => RefusalReasons.Contains(reason);
}

/// <summary>
/// The rules a Release Plan record must satisfy, in one place.
///
/// <para>
/// <b>Pure and total.</b> No I/O, no clock, no ambient state: the writer validates with it, the release gate
/// assesses with it, and a test can re-derive every answer. This is the same construction as
/// <see cref="ReleaseCertificationGate"/> one level up and for the same reason — a rule that can be
/// re-derived from a record is a rule someone else can check.
/// </para>
///
/// <para>
/// <b>Two questions, two methods.</b> <see cref="Validate"/> answers "may this be written?" and is what the
/// governed writer enforces. <see cref="AssessReadiness"/> answers "may this release proceed into this
/// environment?" and is what the release and deployment gates consume. Collapsing them would either refuse
/// to record an honest unfinished state or let an unfinished state read as ready.
/// </para>
/// </summary>
public static class ReleaseSecurityPlanValidation
{
    /// <summary>
    /// Every structural and coherence rule, and the contradictions between members.
    ///
    /// <para>
    /// Deliberately <b>not</b> a readiness judgement: see <see cref="AssessReadiness"/>. What is here is
    /// everything that would make the record itself incoherent — two mechanisms claimed at once, a decision
    /// named against a state it does not describe, a mechanism used without the facts it rests on.
    /// </para>
    /// </summary>
    public static ReleaseSecurityPlanVerdict Validate(ReleaseSecurityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var reasons = new List<ReleasePlanRefusalReason>();
        var detail = new List<string>();

        void Refuse(ReleasePlanRefusalReason reason, string message)
        {
            reasons.Add(reason);
            detail.Add(message);
        }

        // ---- 1. Identity and shape --------------------------------------------------------------------
        if (plan.ReleaseId is null || !ReleaseId.IsValid(plan.ReleaseId.Value))
        {
            Refuse(ReleasePlanRefusalReason.ReleaseIdMissing, "The plan names no valid release id, so nothing can look it up.");
        }

        if (plan.SchemaVersion < MinSchema(plan) || plan.SchemaVersion > MaxSchema(plan))
        {
            Refuse(
                ReleasePlanRefusalReason.SchemaVersionUnsupported,
                $"The record's schema version {plan.SchemaVersion} is outside {MinSchema(plan)}-{MaxSchema(plan)}.");
        }

        if (plan.Version < 1)
        {
            Refuse(
                ReleasePlanRefusalReason.PlanVersionNotPositive,
                $"A plan is versioned from 1; this record is version {plan.Version}.");
        }

        // ---- 2. Provenance -----------------------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(plan.UpdatedBy))
        {
            Refuse(ReleasePlanRefusalReason.ProvenanceMissing, "No actor is recorded against this version.");
        }

        if (string.IsNullOrWhiteSpace(plan.Reason))
        {
            Refuse(ReleasePlanRefusalReason.UpdateReasonMissing, "No reason is recorded for this version.");
        }

        if (plan.UpdatedAt is null)
        {
            Refuse(ReleasePlanRefusalReason.UpdateTimestampMissing, "No timestamp is recorded for this version.");
        }

        // ---- 3. C-1 — rotation -------------------------------------------------------------------------
        var hasRotationDecision = !string.IsNullOrWhiteSpace(plan.CredentialRotationDecisionReference);

        if (plan.CredentialRotationStatus == CredentialRotationStatus.Confirmed)
        {
            if (!hasRotationDecision)
            {
                Refuse(
                    ReleasePlanRefusalReason.RotationConfirmationNotRecorded,
                    "Credential rotation is recorded as confirmed with no decision named. A confirmation with no decision "
                    + "behind it is indistinguishable from an assertion.");
            }

            if (plan.HumanDecisionAuthority != DeploymentAuthorityRole.Owner)
            {
                Refuse(
                    ReleasePlanRefusalReason.RotationConfirmationNotOwnerAuthorized,
                    $"Credential rotation is recorded as confirmed with authority '{Describe(plan.HumanDecisionAuthority)}'. "
                    + "Rotation is a Human Owner act (CONFIGURATION_STANDARDS.md 13.2) and no other role may record it.");
            }
        }
        else if (hasRotationDecision)
        {
            Refuse(
                ReleasePlanRefusalReason.RotationDecisionReferenceOnUnconfirmedState,
                $"A rotation decision is named ('{plan.CredentialRotationDecisionReference}') while the status is "
                + $"'{plan.CredentialRotationStatus}'. A decision recorded against a state it does not describe cannot be reviewed.");
        }

        // ---- 4. C-2 — the protection mechanism, and the contradictions between the two -----------------
        switch (plan.ReleaseReferenceProtectionMode)
        {
            case ReleaseReferenceProtectionMode.None:
                if (plan.AllowedEnvironmentScope != ReleaseReferenceProtectionScope.None)
                {
                    Refuse(
                        ReleasePlanRefusalReason.ScopeRecordedWithoutAMechanism,
                        $"An environment scope ('{plan.AllowedEnvironmentScope}') is recorded while no protection mechanism is. "
                        + "A scope with nothing to scope reads as a decision that was never made.");
                }

                // Not refused here. A plan recording no mechanism is an honest unfinished state and must be
                // storable; it is refused by AssessReadiness, where the failure can say so.
                break;

            case ReleaseReferenceProtectionMode.ServerSideProtected:
                if (!plan.ReleaseRefServerSideProtectionVerified)
                {
                    Refuse(
                        ReleasePlanRefusalReason.ServerSideProtectionNotVerified,
                        "Server-side protection is recorded as the mechanism without an authorized check having observed a ruleset. "
                        + "No local process can establish this; it is a statement about the remote's settings, and it is not assumed.");
                }

                if (plan.CompensatingControlApproved || plan.CompensatingControlVerified)
                {
                    Refuse(
                        ReleasePlanRefusalReason.CompensatingControlClaimsServerSideProtection,
                        "The record claims server-side protection while also recording the C-2 compensating control as approved or "
                        + "verified. They are different mechanisms with different scopes and different failure modes, and one release "
                        + "may rely on one of them. Recording both is how a substitute starts reporting itself as the control it replaced.");
                }

                if (plan.AllowedEnvironmentScope == ReleaseReferenceProtectionScope.DevTest)
                {
                    Refuse(
                        ReleasePlanRefusalReason.ServerSideProtectionCannotBeScopedToDevTest,
                        "Server-side protection is scoped to DEV/TEST. A ruleset is a property of the repository and applies wherever "
                        + "the reference is deployed, so this scope misdescribes it.");
                }

                break;

            case ReleaseReferenceProtectionMode.GovernedCompensatingControl:
                if (!plan.CompensatingControlApproved)
                {
                    Refuse(
                        ReleasePlanRefusalReason.CompensatingControlNotApproved,
                        "The C-2 compensating control is recorded as the mechanism but is not recorded as approved. "
                        + "The Owner's approval is the whole of this mechanism.");
                }

                if (!plan.CompensatingControlVerified)
                {
                    Refuse(
                        ReleasePlanRefusalReason.CompensatingControlNotVerified,
                        "The C-2 compensating control is approved but its rules were never recorded as verified against this "
                        + "release's reference. An approved control that was never exercised is a plan, not a control.");
                }

                if (plan.AllowedEnvironmentScope == ReleaseReferenceProtectionScope.None)
                {
                    Refuse(
                        ReleasePlanRefusalReason.CompensatingControlScopeMissing,
                        "The C-2 compensating control is recorded with no environment scope, so nothing says where it applies. "
                        + "A substitute whose limits are unstated is read as covering everything.");
                }
                else if (plan.AllowedEnvironmentScope != ReleaseReferenceProtectionScope.DevTest)
                {
                    Refuse(
                        ReleasePlanRefusalReason.CompensatingControlScopeExceedsDevTest,
                        $"The C-2 compensating control claims scope '{plan.AllowedEnvironmentScope}'. It is approved for "
                        + $"{DeploymentEnvironmentId.Dev} and {DeploymentEnvironmentId.Test} only, and the record is refused rather than "
                        + "narrowed: silently treating it as DEV/TEST would leave the record saying one thing and the behaviour doing another.");
                }

                if (plan.ReleaseRefServerSideProtectionVerified)
                {
                    Refuse(
                        ReleasePlanRefusalReason.CompensatingControlClaimsServerSideProtection,
                        "The record relies on the C-2 compensating control while also claiming that a remote ruleset was observed. "
                        + "No ruleset exists on these repositories; the claim would make the substitute report itself as the control it replaced.");
                }

                if (string.IsNullOrWhiteSpace(plan.HumanDecisionReference)
                    || plan.HumanDecisionAuthority != DeploymentAuthorityRole.Owner)
                {
                    Refuse(
                        ReleasePlanRefusalReason.ProtectionMechanismNotOwnerAuthorized,
                        "The recorded protection mechanism is not backed by a Human Owner decision: "
                        + $"decision reference {(string.IsNullOrWhiteSpace(plan.HumanDecisionReference) ? "is absent" : "is present")}, "
                        + $"authority '{Describe(plan.HumanDecisionAuthority)}'. The C-2 deviation is the Owner's to grant.");
                }

                break;

            default:
                Refuse(
                    ReleasePlanRefusalReason.NoProtectionMechanismRecorded,
                    $"'{plan.ReleaseReferenceProtectionMode}' is not a protection mechanism this contract understands.");
                break;
        }

        return reasons.Count > 0
            ? ReleaseSecurityPlanVerdict.Refused([.. reasons], detail)
            : ReleaseSecurityPlanVerdict.Valid(
                ["Every well-formedness and coherence rule passed."]);
    }

    /// <summary>
    /// Whether the plan permits a release to proceed into <paramref name="environment"/>.
    ///
    /// <para>
    /// <b>This is the gate's question, and it is asked per environment.</b> The same recorded C-2 deviation
    /// is sufficient for ENV-DEV and ENV-TEST and insufficient for ENV-PROD; an assessment that could not see
    /// the target would have to either refuse the DEV deployment it was approved for or permit the PROD one it
    /// was not. The protection half is delegated to
    /// <see cref="ReleaseReferenceProtectionEvidence.Judge"/>, so this cannot disagree with the certification
    /// and deployment gates about whether the reference is protected.
    /// </para>
    /// </summary>
    public static ReleaseSecurityReadiness AssessReadiness(ReleaseSecurityPlan? plan, DeploymentEnvironmentId environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (plan is null)
        {
            return ReleaseSecurityReadiness.NotReady(
                environment,
                [ReleasePlanRefusalReason.NoProtectionMechanismRecorded, ReleasePlanRefusalReason.RotationConfirmationNotRecorded],
                [
                    "No governed release plan is recorded for this release, so neither the credential rotation nor the "
                    + "release-reference protection has been established. Absence is not a pass."
                ]);
        }

        var reasons = new List<ReleasePlanRefusalReason>();
        var detail = new List<string>();

        if (plan.CredentialRotationStatus != CredentialRotationStatus.Confirmed)
        {
            // One typed reason for both unfinished states, because the refusal is the same refusal: rotation
            // is not confirmed. Which of the two it is travels in the detail, where it belongs — a reader
            // deciding what to do needs "which state", and a caller branching needs "refused".
            reasons.Add(ReleasePlanRefusalReason.RotationConfirmationNotRecorded);

            detail.Add(plan.CredentialRotationStatus switch
            {
                CredentialRotationStatus.Unknown =>
                    "CREDENTIAL_ROTATION_UNCONFIRMED: the W9.0 committed-credential exposure has not been recorded as rotated or revoked.",
                CredentialRotationStatus.Required =>
                    "CREDENTIAL_ROTATION_OUTSTANDING: the W9.0 committed-credential exposure is recorded as not yet rotated or revoked.",
                _ => "CREDENTIAL_ROTATION_OUTSTANDING: the credential rotation is not recorded as confirmed."
            });
        }

        if (plan.ReleaseReferenceProtectionMode == ReleaseReferenceProtectionMode.None)
        {
            // Named locally so the refusal reads as "nothing was recorded" rather than as a mechanism that
            // failed. A gate that reported the two identically would send a reader looking for a defect in a
            // release whose only problem is that nobody has recorded anything yet.
            reasons.Add(ReleasePlanRefusalReason.NoProtectionMechanismRecorded);
            detail.Add(
                $"{ReleaseReferenceProtectionJudgement.UnrecordedCode}: no protection mechanism for the release-tag namespace "
                + "has been recorded, so the reference's protection is unestablished. No mechanism means no release security readiness.");
        }

        var protection = plan.ToProtectionEvidence().Judge(environment);

        if (!protection.IsSatisfied)
        {
            reasons.Add(protection.IsUnobserved
                ? ReleasePlanRefusalReason.NoProtectionMechanismRecorded
                : ReleasePlanRefusalReason.ProtectionMechanismNotOwnerAuthorized);

            detail.Add(protection.Detail);
        }

        return reasons.Count > 0
            ? ReleaseSecurityReadiness.NotReady(environment, [.. reasons], detail)
            : ReleaseSecurityReadiness.Ready(
                environment,
                ["Credential rotation is confirmed and the recorded reference protection covers this environment.", protection.Detail]);
    }

    private static int MinSchema(ReleaseSecurityPlan plan) => ReleaseSecurityPlan.MinSchemaVersion;

    private static int MaxSchema(ReleaseSecurityPlan plan) => ReleaseSecurityPlan.MaxSchemaVersion;

    private static string Describe(DeploymentAuthorityRole? role)
        => role?.ToString() ?? "(none)";
}
