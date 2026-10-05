using System.Text;
using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// The one thing a deployment is allowed to conclude about a target database's schema.
///
/// <para>
/// <b>Five tokens, and no sixth.</b> A deployment step must not be able to say "roughly fine" or
/// "probably safe" — the vocabulary is closed so that every outcome a pipeline can branch on is one a
/// reader can look up. <see cref="MigrationApproved"/> is the only token that permits <i>applying</i>
/// anything; <see cref="NoMigrationRequired"/> permits a deployment with no schema step at all, and it
/// is a decision rather than an absence (see <see cref="MigrationGovernance"/> rule 1).
/// </para>
///
/// <para>
/// <b>Why "unestablished" is not its own token.</b> The estate's habit elsewhere is to give an unknown
/// its own state (see <see cref="MigrationRequirementState.Unknown"/>), and that is right where the
/// unknown is a <i>recorded fact</i>. Here it is not: a caller is asking <i>what may I do</i>, and the
/// answer to "I could not establish it" is a refusal. Folding it into
/// <see cref="MigrationIncompatible"/> under a typed
/// <see cref="MigrationGovernanceRefusalReason.RequirementStateUnestablished"/> keeps the refusal
/// visible in the reason while keeping the branch set closed.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationGovernanceDecision
{
    /// <summary>No schema step applies to this release on this target, and that was established by observation.</summary>
    NoMigrationRequired,

    /// <summary>The migration set may be applied to this target under the recorded authorization.</summary>
    MigrationApproved,

    /// <summary>The target holds data the release would alter and no verified backup discharges the obligation.</summary>
    BackupRequired,

    /// <summary>
    /// A migration in this release's own set removes or narrows schema in its forward path, and no Owner
    /// authorization covers it. The remedy is an Owner decision or a new migration — never a quiet run.
    /// </summary>
    DestructiveMigrationBlocked,

    /// <summary>
    /// The release and the target cannot be reconciled: the migration set does not agree with the target's
    /// applied history, or a condition that must be established could not be. <b>Not a deployment.</b>
    /// </summary>
    MigrationIncompatible
}

/// <summary>
/// Why a judgement is not <see cref="MigrationGovernanceDecision.MigrationApproved"/> (or, for the first
/// member, why an approval would have been an overstatement).
///
/// <para>
/// Typed rather than prose, because these are the values a pipeline branches on and an alert fires on.
/// One reason is recorded per judgement at minimum; several may apply at once and all are recorded,
/// because a target that is simultaneously unreachable and unauthorized should say both.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationGovernanceRefusalReason
{
    /// <summary>The release's own migration assessment is <see cref="MigrationRequirementState.Unknown"/>.</summary>
    RequirementStateUnestablished,

    /// <summary>
    /// The assessment says a migration set applies but names no set, or says none applies but names one.
    /// Either way the release disagrees with itself.
    /// </summary>
    MigrationSetIdentityMismatch,

    /// <summary>The target was not successfully inspected, so nothing about its schema is established.</summary>
    TargetUnreachable,

    /// <summary>The target's applied history is not a prefix of the release's migration set.</summary>
    TargetAhead,

    /// <summary>Same length, different identities. This is an investigation, not a deployment.</summary>
    TargetDivergent,

    /// <summary>Whether the target holds data could not be established, so the backup obligation stands.</summary>
    TargetDataStateUnknown,

    /// <summary>The target holds data, the release would alter schema, and no verified backup exists.</summary>
    BackupNotVerified,

    /// <summary>The recorded authorization does not cover this environment.</summary>
    EnvironmentOutsideApprovedScope,

    /// <summary>Migrations would run and no authorization was recorded at all.</summary>
    AuthorizationMissing,

    /// <summary>Migrations would run and the recorded role may not deploy them to this environment.</summary>
    AuthorizationRoleInsufficient,

    /// <summary>A destructive forward migration is present and the recorded authorization is not the Owner's.</summary>
    DestructiveMigrationWithoutOwnerAuthorization,

    /// <summary>
    /// The target was reported <see cref="MigrationTargetDataState.Empty"/>, but the emptiness was not
    /// established by a deterministic count. A planner statistic is not proof that a database is empty —
    /// it is reset by a cluster restart — so the backup obligation stands.
    /// </summary>
    EmptinessNotDeterministicallyProven
}

/// <summary>
/// Whether the target holds rows a migration could alter. <b>Three states, not a boolean</b>, and this is
/// load-bearing: <see cref="Unknown"/> and <see cref="Empty"/> are the two values a boolean would collapse,
/// and they demand opposite responses — an unobserved database must be treated as though it holds data,
/// while an observed-empty one discharges the backup obligation by evidence.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationTargetDataState
{
    /// <summary>Not established. The backup obligation therefore stands; it is not discharged by silence.</summary>
    Unknown,

    /// <summary>Observed to hold no rows in any table the release would touch. Recorded with the observation's basis.</summary>
    Empty,

    /// <summary>Observed to hold rows. <see cref="MigrationGovernanceDecision.BackupRequired"/> unless a backup is verified.</summary>
    HoldsData
}

/// <summary>
/// <b>How an emptiness claim was established.</b> The member that makes "the target is empty" a fact rather
/// than an inference — and the member whose absence let a backup obligation be discharged by a number that
/// a container restart had reset to zero.
///
/// <para>
/// <b>Why this is its own type rather than a sentence in <see cref="MigrationTargetObservation.Basis"/>.</b>
/// The basis is free text: good for a reader, useless to a decision. A judgement cannot refuse a claim it
/// can only read prose about. Typing the mechanism is what lets the rule be enforced by the contract
/// instead of by whoever last edited a string.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationTargetDataStateBasis
{
    /// <summary>Not established. The safe default for any caller that did not say how it knows.</summary>
    Unestablished,

    /// <summary>
    /// Planner statistics — <c>pg_stat_user_tables.n_live_tup</c> and its kin. <b>Sufficient to report rows
    /// present, never sufficient to report a database empty.</b> These counters live in shared memory and a
    /// cluster restart resets them to zero, which makes a database holding 8,500 rows read as empty.
    /// </summary>
    StatisticsEstimate,

    /// <summary>
    /// An exact row count over a policy-defined table set, read from the database itself. The only basis on
    /// which emptiness may be asserted.
    /// </summary>
    DeterministicCount
}

/// <summary>
/// <b>The policy for classifying a target's data state, in one place.</b>
///
/// <para>
/// Extracted so that "statistics cannot prove emptiness" is a property of shared code with a test beside it,
/// rather than a rule each observer re-implements and each is free to get subtly wrong. The W9.4 driver got
/// it wrong exactly once, and the cost was a release-recorded backup obligation discharged without a backup.
/// </para>
///
/// <para>
/// <b>The estimate is deliberately not a parameter.</b> A signature that accepted an estimate would invite
/// a caller to pass one, and there is no decision this type may take on one. Statistics belong in the
/// observation's <see cref="MigrationTargetObservation.Basis"/> for a human to read, not in the classifier.
/// </para>
/// </summary>
public static class MigrationTargetDataStatePolicy
{
    /// <summary>
    /// Classifies from the <b>only</b> admissible input: the artifact of an exact count.
    ///
    /// <list type="bullet">
    /// <item><see langword="null"/> — the count could not be performed. The answer is
    /// <see cref="MigrationTargetDataState.Unknown"/>, which keeps the backup obligation standing rather
    /// than discharging it by silence. An unobserved database is not an empty one.</item>
    /// <item><c>0</c> — <see cref="MigrationTargetDataState.Empty"/>, and only here.</item>
    /// <item>anything else — <see cref="MigrationTargetDataState.HoldsData"/>.</item>
    /// </list>
    /// </summary>
    public static MigrationTargetDataState ClassifyFromDeterministicCount(long? exactRowCount)
        => exactRowCount switch
        {
            null => MigrationTargetDataState.Unknown,
            0 => MigrationTargetDataState.Empty,
            _ => MigrationTargetDataState.HoldsData
        };

    /// <summary>The basis that accompanies each state this policy can produce.</summary>
    public static MigrationTargetDataStateBasis BasisFor(long? exactRowCount)
        => exactRowCount is null
            ? MigrationTargetDataStateBasis.Unestablished
            : MigrationTargetDataStateBasis.DeterministicCount;
}

/// <summary>
/// What a target database actually looked like when it was inspected, in the terms the judgement needs.
///
/// <para>
/// <b>The basis is required and non-empty.</b> Every member here is a claim about a machine that the
/// judgement cannot see. A record with no basis is an assertion, and an assertion is what the whole
/// compensating-control posture exists to replace — so the constructor refuses one rather than trusting
/// the caller to supply it.
/// </para>
/// </summary>
public sealed record MigrationTargetObservation
{
    public MigrationTargetObservation(
        DeploymentEnvironmentId environment,
        bool reachable,
        bool schemaPresent,
        IReadOnlyList<string> appliedMigrationIds,
        MigrationTargetDataState dataState,
        bool backupVerified,
        string basis,
        MigrationTargetDataStateBasis dataStateBasis = MigrationTargetDataStateBasis.Unestablished)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(appliedMigrationIds);

        if (string.IsNullOrWhiteSpace(basis))
        {
            throw new ArgumentException(
                "A target observation must record what it was established from. An unobserved target and an empty one produce the same member values and demand opposite decisions.",
                nameof(basis));
        }

        Environment = environment;
        Reachable = reachable;
        SchemaPresent = schemaPresent;
        AppliedMigrationIds = [.. appliedMigrationIds];
        DataState = dataState;
        DataStateBasis = dataStateBasis;
        BackupVerified = backupVerified;
        Basis = basis;
    }

    /// <summary>The environment that was inspected. Never inherited from the request — it is what was actually read.</summary>
    public DeploymentEnvironmentId Environment { get; }

    /// <summary>True when the target answered. False means nothing else here was established.</summary>
    public bool Reachable { get; }

    /// <summary>True when the target carries a migration history table.</summary>
    public bool SchemaPresent { get; }

    /// <summary>The applied migration identities, in apply order, as the target reports them.</summary>
    public IReadOnlyList<string> AppliedMigrationIds { get; }

    public MigrationTargetDataState DataState { get; }

    /// <summary>
    /// How <see cref="DataState"/> was established. Defaults to
    /// <see cref="MigrationTargetDataStateBasis.Unestablished"/>, which is the safe value: a caller that does
    /// not say how it knows the target is empty has not established that it is, and the judgement refuses to
    /// discharge a backup obligation on it.
    /// </summary>
    public MigrationTargetDataStateBasis DataStateBasis { get; }

    /// <summary>
    /// True only when a backup of this target was taken <i>and</i> verified. A backup that was taken and
    /// not verified is not a verified backup, which is why this is not named "backupTaken".
    /// </summary>
    public bool BackupVerified { get; }

    public string Basis { get; }

    /// <summary>
    /// The same observation with the backup obligation answered by a backup that was taken and verified.
    ///
    /// <para>
    /// <b>This cannot be used to weaken the observation, only to add one fact to it.</b> Every other member
    /// is carried through unchanged by construction — there is no parameter to pass a different reachability,
    /// schema state, applied set or data state — which is why this is a method on the type rather than a
    /// <c>with</c> expression at the call site. A caller re-listing the seven members to change one of them
    /// is a caller that can silently drop one, and the member most likely to be dropped in that edit is
    /// <see cref="DataState"/>, which is the member the whole obligation turns on.
    /// </para>
    ///
    /// <para>
    /// <b>An unverified backup cannot be recorded through this method.</b> It refuses, rather than returning
    /// an observation whose <see cref="BackupVerified"/> is false while its basis claims a backup was taken
    /// — the exact shape of an unverified claim.
    /// </para>
    /// </summary>
    public MigrationTargetObservation WithVerifiedBackup(string verificationBasis)
    {
        if (string.IsNullOrWhiteSpace(verificationBasis))
        {
            throw new ArgumentException(
                "Discharging a backup obligation requires recording what verified the backup. A verified backup with no stated verification is an assertion.",
                nameof(verificationBasis));
        }

        return new MigrationTargetObservation(
            Environment,
            Reachable,
            SchemaPresent,
            AppliedMigrationIds,
            DataState,
            backupVerified: true,
            $"{Basis} {verificationBasis}",
            DataStateBasis);
    }
}

/// <summary>
/// The judgement: what a deployment may do to this target, why, and what it was decided from.
///
/// <para>
/// <b>Pure and total.</b> No I/O, no clock, no environment access — every input is a parameter, so the
/// same request always yields the same verdict and the verdict can be tested exhaustively. This is the
/// same discipline <c>DeploymentStateMachine</c> and <c>ReleaseLifecycleMachine</c> already hold to, and
/// it is what makes <see cref="MigrationGovernanceVerdict.CanonicalForm"/> meaningful evidence rather
/// than a formatted log line.
/// </para>
/// </summary>
public sealed record MigrationGovernanceVerdict
{
    private MigrationGovernanceVerdict(
        MigrationGovernanceDecision decision,
        IReadOnlyList<MigrationGovernanceRefusalReason> refusalReasons,
        MigrationCompatibility? observedCompatibility,
        IReadOnlyList<string> detail)
    {
        Decision = decision;
        RefusalReasons = refusalReasons;
        ObservedCompatibility = observedCompatibility;
        Detail = detail;
    }

    public MigrationGovernanceDecision Decision { get; }

    /// <summary>
    /// Empty <b>only</b> for <see cref="MigrationGovernanceDecision.NoMigrationRequired"/> and
    /// <see cref="MigrationGovernanceDecision.MigrationApproved"/>.
    /// </summary>
    public IReadOnlyList<MigrationGovernanceRefusalReason> RefusalReasons { get; }

    /// <summary>
    /// How the target's applied history compared to the release's set, as observed. Null when no migration
    /// applies or no comparison was made — never a defaulted <see cref="MigrationCompatibility.Match"/>,
    /// because a decoded absence read as a match is a defect this estate has already paid for once.
    /// </summary>
    public MigrationCompatibility? ObservedCompatibility { get; }

    /// <summary>What the decision rests on, in the terms of the facts that produced it.</summary>
    public IReadOnlyList<string> Detail { get; }

    /// <summary>
    /// True only for the two tokens that permit a deployment to proceed.
    /// <b>Deliberately not named "IsSuccess"</b>: a caller that reads a boolean will not read the reason.
    /// </summary>
    public bool PermitsDeployment =>
        Decision is MigrationGovernanceDecision.NoMigrationRequired or MigrationGovernanceDecision.MigrationApproved;

    /// <summary>True when the target must be backed up and no verified backup discharges the obligation.</summary>
    public bool RequiresBackup => Decision == MigrationGovernanceDecision.BackupRequired;

    /// <summary>True when the remedy is an Owner decision rather than a target-side action.</summary>
    public bool RequiresOwner =>
        Decision is MigrationGovernanceDecision.DestructiveMigrationBlocked;

    /// <summary>
    /// The decision in a form that changes when any input to it changes. Used for evidence and for tests
    /// that assert non-vacuity: if a mutation to the judgement does not move this, the judgement is not
    /// reading the member the mutation touched.
    /// </summary>
    public string CanonicalForm()
    {
        var builder = new StringBuilder(Decision.ToString()).Append('\0')
            .Append(string.Join(",", RefusalReasons.Select(r => r.ToString()))).Append('\0')
            .Append(ObservedCompatibility?.ToString() ?? string.Empty).Append('\0');

        foreach (var line in Detail)
        {
            builder.Append(line).Append('\0');
        }

        return builder.ToString();
    }

    public ArtifactDigest ComputeDigest() => ArtifactDigest.Compute(Encoding.UTF8.GetBytes(CanonicalForm()));

    public override string ToString()
        => RefusalReasons.Count == 0
            ? Decision.ToString()
            : $"{Decision} ({string.Join(", ", RefusalReasons)})";

    internal static MigrationGovernanceVerdict Of(
        MigrationGovernanceDecision decision,
        IReadOnlyList<MigrationGovernanceRefusalReason> refusalReasons,
        MigrationCompatibility? observedCompatibility,
        IReadOnlyList<string> detail)
        => new(decision, refusalReasons, observedCompatibility, detail);
}

/// <summary>
/// Everything the judgement is allowed to consider. A record rather than a parameter list, so that adding a
/// fact is a compile-visible change at every construction site instead of a silently defaulted argument.
/// </summary>
public sealed record MigrationGovernanceRequest
{
    public MigrationGovernanceRequest(
        MigrationAssessment assessment,
        DeploymentEnvironmentId environment,
        MigrationTargetObservation observation,
        IReadOnlyList<string> destructiveForwardMigrationIds,
        DeploymentAuthorization? authorization)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(destructiveForwardMigrationIds);

        Assessment = assessment;
        Environment = environment;
        Observation = observation;
        DestructiveForwardMigrationIds = [.. destructiveForwardMigrationIds];
        Authorization = authorization;
    }

    /// <summary>The release's own recorded expectation, quoted rather than re-derived.</summary>
    public MigrationAssessment Assessment { get; }

    /// <summary>The environment being deployed to. Used for scope, never for convenience.</summary>
    public DeploymentEnvironmentId Environment { get; }

    public MigrationTargetObservation Observation { get; }

    /// <summary>
    /// The migrations in this release's own set whose <b>forward</b> path removes or narrows schema.
    /// Measured from the migration sources, not inferred from a Down method's existence: a migration that
    /// drops a column on the way up is destructive regardless of whether it can be undone.
    /// </summary>
    public IReadOnlyList<string> DestructiveForwardMigrationIds { get; }

    public DeploymentAuthorization? Authorization { get; }
}

/// <summary>
/// The single place a migration decision is made, for the same reason the C-2 judgement has a single home:
/// two implementations of a control is one control and one divergent copy.
///
/// <para><b>The rules, in the order they are applied, and why that order.</b></para>
/// <list type="number">
/// <item><description>
/// <b>Consistency of the release's own statement first.</b> An assessment that names no set, or names one
/// while saying none applies, is refused before anything about a target matters. A release that disagrees
/// with itself cannot be deployed to any database, so the target is not consulted.
/// </description></item>
/// <item><description>
/// <b>No migration applies.</b> Returns <see cref="MigrationGovernanceDecision.NoMigrationRequired"/> —
/// but only after the target has actually been reached. "There is nothing to do" is a claim about a target
/// too, and the one way to make it cheaply and wrongly is to conclude it without looking.
/// </description></item>
/// <item><description>
/// <b>Unestablished.</b> <see cref="MigrationRequirementState.Unknown"/> is
/// <see cref="MigrationGovernanceDecision.MigrationIncompatible"/>; it is never treated as "none".
/// </description></item>
/// <item><description>
/// <b>Destructive forward migrations before compatibility.</b> If the release intends to remove schema,
/// that is a fact about the <i>release</i> and does not depend on what the target looks like. Deciding it
/// here means the Owner is asked even when the target happens to be empty, which is the only ordering in
/// which a destructive migration cannot slip through on an incidental "the target was fresh anyway".
/// </description></item>
/// <item><description>
/// <b>Compatibility, computed against the observed history.</b> Ahead and divergent are refused. Behind is
/// the ordinary case this whole mechanism exists for.
/// </description></item>
/// <item><description>
/// <b>Authorization, only when migrations would actually run.</b> A target already at the release's schema
/// needs no authorization to be left alone; requiring one would train its readers to authorize reflexively.
/// </description></item>
/// <item><description>
/// <b>The backup obligation last</b>, because it is the only rule a verified backup can discharge — and it
/// must be reached only when everything else already permits the deployment, so that the remedy it names is
/// the remedy that is actually owed.
/// </description></item>
/// </list>
///
/// <para>
/// <b>All failures are collected, not short-circuited, within a rule group.</b> A target that is both
/// unreachable and unauthorized reports both, because a reader fixing one should not have to re-run to
/// discover the other.
/// </para>
/// </summary>
public static class MigrationGovernance
{
    public static MigrationGovernanceVerdict Judge(MigrationGovernanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reasons = new List<MigrationGovernanceRefusalReason>();
        var detail = new List<string>();

        var assessment = request.Assessment;
        var metadata = assessment.Metadata;
        var observation = request.Observation;

        // ---- rule 1: the release's own statement must be coherent ------------------------------------
        if (assessment.State == MigrationRequirementState.MigrationsRequired && metadata.IsNone)
        {
            reasons.Add(MigrationGovernanceRefusalReason.MigrationSetIdentityMismatch);
            detail.Add("The release records MIGRATIONS_REQUIRED but names no migration set, so there is nothing to apply and nothing to compare a target against.");
        }

        if (assessment.State == MigrationRequirementState.NoDatabaseMigration && !metadata.IsNone)
        {
            reasons.Add(MigrationGovernanceRefusalReason.MigrationSetIdentityMismatch);
            detail.Add($"The release records NO_DATABASE_MIGRATION but names the migration set '{metadata.Provider}', so the record disagrees with itself.");
        }

        if (reasons.Count > 0)
        {
            return Incompatible(reasons, detail, observedCompatibility: null);
        }

        // ---- rule 2: nothing to do, established by looking --------------------------------------------
        if (assessment.State == MigrationRequirementState.NoDatabaseMigration)
        {
            if (!observation.Reachable)
            {
                reasons.Add(MigrationGovernanceRefusalReason.TargetUnreachable);
                detail.Add("No migration applies, but the target was not reached, so even the empty case is unobserved. " + observation.Basis);
                return Incompatible(reasons, detail, observedCompatibility: null);
            }

            detail.Add("No migration applies to this release and the target was reached and observed. " + observation.Basis);
            return Verdict(MigrationGovernanceDecision.NoMigrationRequired, [], null, detail);
        }

        // ---- rule 3: unestablished is a refusal, never a "none" ----------------------------------------
        if (assessment.State == MigrationRequirementState.Unknown)
        {
            reasons.Add(MigrationGovernanceRefusalReason.RequirementStateUnestablished);
            detail.Add("The release's migration requirement was never established: " + assessment.Basis);
            return Incompatible(reasons, detail, observedCompatibility: null);
        }

        // ---- rule 3b: the target must have been reached -------------------------------------------------
        if (!observation.Reachable)
        {
            reasons.Add(MigrationGovernanceRefusalReason.TargetUnreachable);
            detail.Add("The target did not answer, so its applied migration history is unknown. " + observation.Basis);
            return Incompatible(reasons, detail, observedCompatibility: null);
        }

        // ---- rule 4: destructive forward migrations need the Owner --------------------------------------
        if (request.DestructiveForwardMigrationIds.Count > 0)
        {
            var owner = request.Authorization?.Role == DeploymentAuthorityRole.Owner;

            if (!owner)
            {
                reasons.Add(MigrationGovernanceRefusalReason.DestructiveMigrationWithoutOwnerAuthorization);
                detail.Add(
                    $"This release's own set removes or narrows schema in its forward path: {string.Join(", ", request.DestructiveForwardMigrationIds)}. "
                    + "Only the Owner may authorize a destructive migration, and the recorded authorization is "
                    + (request.Authorization is null ? "absent." : $"'{request.Authorization.Role}'."));
                return Verdict(MigrationGovernanceDecision.DestructiveMigrationBlocked, reasons, observedCompatibility: null, detail);
            }

            detail.Add(
                $"Destructive forward migrations {string.Join(", ", request.DestructiveForwardMigrationIds)} are covered by an Owner authorization "
                + $"recorded at {request.Authorization!.AuthorizedAt:O}.");
        }

        // ---- rule 5: compatibility, computed against what the target reported ---------------------------
        var compatibility = Compare(metadata.MigrationIds, observation.AppliedMigrationIds, out var comparisonDetail);
        detail.Add(comparisonDetail);

        switch (compatibility)
        {
            case MigrationCompatibility.Ahead:
                reasons.Add(MigrationGovernanceRefusalReason.TargetAhead);
                detail.Add("The target has been migrated by a newer release. Running this release against it is the forward-only equivalent of a downgrade.");
                return Incompatible(reasons, detail, compatibility);

            case MigrationCompatibility.Divergent:
                reasons.Add(MigrationGovernanceRefusalReason.TargetDivergent);
                detail.Add("The target's applied set is neither a prefix of nor an extension of this release's set. This is an investigation, not a deployment.");
                return Incompatible(reasons, detail, compatibility);

            case MigrationCompatibility.Match:
            case MigrationCompatibility.Behind:
                break;

            default:
                // A future member must fail closed rather than fall through to the approved path.
                reasons.Add(MigrationGovernanceRefusalReason.TargetDivergent);
                detail.Add($"The target comparison produced a state this judgement does not understand: {compatibility}.");
                return Incompatible(reasons, detail, compatibility);
        }

        // ---- rule 6: authorization, but only when migrations would run ----------------------------------
        var pending = metadata.MigrationIds.Count - observation.AppliedMigrationIds.Count;

        if (pending > 0)
        {
            switch (request.Authorization)
            {
                case null:
                    reasons.Add(MigrationGovernanceRefusalReason.AuthorizationMissing);
                    detail.Add($"{pending} migration(s) would run against {request.Environment} and no authorization was recorded.");
                    break;

                case { Role: DeploymentAuthorityRole.Builder }:
                    reasons.Add(MigrationGovernanceRefusalReason.AuthorizationRoleInsufficient);
                    detail.Add($"'{request.Authorization.Role}' may not deploy. The pipeline builds and registers; it does not migrate a target.");
                    break;

                case { Role: DeploymentAuthorityRole.OnCall }:
                    reasons.Add(MigrationGovernanceRefusalReason.AuthorizationRoleInsufficient);
                    detail.Add($"'{request.Authorization.Role}' may trigger a rollback and request a quarantine; it may not apply a schema change.");
                    break;

                case { Role: var role } and var authorization when !CoversEnvironment(role, request.Environment):
                    reasons.Add(MigrationGovernanceRefusalReason.EnvironmentOutsideApprovedScope);
                    detail.Add($"'{role}' is not authorized for {request.Environment}.");
                    break;

                default:
                    detail.Add($"{pending} migration(s) would run against {request.Environment}, authorized by '{request.Authorization!.ActorIdentity}' as {request.Authorization.Role} at {request.Authorization.AuthorizedAt:O}.");
                    break;
            }
        }
        else
        {
            detail.Add("The target already carries this release's full migration set, so no migration would run and no authorization is required to leave it alone.");
        }

        if (reasons.Count > 0)
        {
            return Incompatible(reasons, detail, compatibility);
        }

        // ---- rule 7: the backup obligation, discharged only by evidence ----------------------------------
        if (pending > 0 || metadata.MigrationIds.Count > 0)
        {
            switch (observation.DataState)
            {
                case MigrationTargetDataState.Unknown:
                    reasons.Add(MigrationGovernanceRefusalReason.TargetDataStateUnknown);
                    detail.Add(
                        "Whether the target holds data was not established, so the backup obligation recorded by this release (BackupRequired="
                        + $"{assessment.BackupRequired}) stands. An unobserved database is not an empty one. {observation.Basis}");
                    return Verdict(MigrationGovernanceDecision.BackupRequired, reasons, compatibility, detail);

                case MigrationTargetDataState.HoldsData when !observation.BackupVerified:
                    reasons.Add(MigrationGovernanceRefusalReason.BackupNotVerified);
                    detail.Add(
                        "The target holds data, this release alters schema, and no verified backup discharges the obligation the release records "
                        + $"(BackupRequired={assessment.BackupRequired}). A backup that was taken and not verified is not a verified backup.");
                    return Verdict(MigrationGovernanceDecision.BackupRequired, reasons, compatibility, detail);

                case MigrationTargetDataState.HoldsData when observation.BackupVerified:
                    detail.Add("The target holds data and a verified backup discharges the release's recorded backup obligation.");
                    break;

                // ---- rule 7a: emptiness must be a measurement, not an inference --------------------------
                // This case exists because of a measured failure. The W9.4 driver classified a target as
                // Empty from sum(pg_stat_user_tables.n_live_tup) — a planner statistic held in shared memory
                // that a container restart resets to zero. Against that reading the target held 8,500 rows,
                // and the release's recorded backup obligation was discharged without a backup being taken.
                //
                // The guard is placed BEFORE the discharging case rather than inside it, so that a future
                // edit to the discharge message cannot remove the requirement that makes it true.
                case MigrationTargetDataState.Empty
                    when observation.DataStateBasis != MigrationTargetDataStateBasis.DeterministicCount:
                    reasons.Add(MigrationGovernanceRefusalReason.EmptinessNotDeterministicallyProven);
                    detail.Add(
                        "The target is reported empty, but the emptiness was established by "
                        + $"'{observation.DataStateBasis}' rather than by a deterministic count. Planner statistics are reset by a "
                        + "cluster restart, so a database holding millions of rows can read as empty immediately after one — which is "
                        + "exactly the state a re-establishment produces. An emptiness that is inferred rather than measured does not "
                        + $"discharge the obligation this release records (BackupRequired={assessment.BackupRequired}). "
                        + observation.Basis);
                    return Verdict(MigrationGovernanceDecision.BackupRequired, reasons, compatibility, detail);

                case MigrationTargetDataState.Empty:
                    detail.Add(
                        "The target was observed to hold no rows by an exact count over the policy table set, so the release's recorded "
                        + $"backup obligation is discharged by measurement rather than by performing one. This narrows the release's pre-inspection BackupRequired={assessment.BackupRequired}, which was recorded with no target inspected. "
                        + observation.Basis);
                    break;
            }
        }

        detail.Add(
            $"Reversibility for this release is recorded as '{assessment.Reversibility}': "
            + (assessment.Reversibility == MigrationReversibility.Reversible
                ? "an exercised Down path exists."
                : "the remedy for a bad outcome is a forward fix, not a schema reversion."));

        return Verdict(MigrationGovernanceDecision.MigrationApproved, [], compatibility, detail);
    }

    /// <summary>
    /// The target's applied history against the release's set.
    ///
    /// <para>
    /// <b>A prefix relation, not a set operation.</b> Migrations are ordered and each expects its
    /// predecessors, so <c>[a,b]</c> applied against a release expecting <c>[a,b,c]</c> is
    /// <see cref="MigrationCompatibility.Behind"/>, while <c>[a,c]</c> against <c>[a,b,c]</c> is
    /// <see cref="MigrationCompatibility.Divergent"/> even though every id it holds is expected. Comparing
    /// as sets would call the second one "behind" and apply <c>b</c> onto a schema that never saw it in
    /// order — which is the drift this comparison exists to catch.
    /// </para>
    /// </summary>
    private static MigrationCompatibility Compare(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> applied,
        out string detail)
    {
        var shared = 0;
        while (shared < expected.Count && shared < applied.Count
            && string.Equals(expected[shared], applied[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        if (shared == expected.Count && shared == applied.Count)
        {
            detail = $"The target's applied history matches this release's set exactly ({expected.Count} migration(s), in order).";
            return MigrationCompatibility.Match;
        }

        if (shared == applied.Count)
        {
            detail = $"The target is behind by {expected.Count - applied.Count} migration(s); its applied history is an exact prefix of this release's set.";
            return MigrationCompatibility.Behind;
        }

        if (shared == expected.Count)
        {
            detail = $"The target carries {applied.Count - expected.Count} migration(s) beyond this release's set: {string.Join(", ", applied.Skip(expected.Count))}.";
            return MigrationCompatibility.Ahead;
        }

        detail = $"The target's applied history diverges from this release's set at position {shared + 1}: the release expects "
            + $"'{expected[shared]}', the target reports '{applied[shared]}'.";

        return MigrationCompatibility.Divergent;
    }

    /// <summary>
    /// Which environments a role may migrate. Mirrors <see cref="DeploymentAuthorityRole"/>'s own documented
    /// remit rather than inventing a second policy: the delivery team holds ENV-DEV and ENV-TEST, the Owner
    /// holds all three, and the other two roles hold none.
    /// </summary>
    private static bool CoversEnvironment(DeploymentAuthorityRole role, DeploymentEnvironmentId environment) => role switch
    {
        DeploymentAuthorityRole.Owner => true,
        DeploymentAuthorityRole.DeliveryTeam =>
            environment == DeploymentEnvironmentId.DevEnv || environment == DeploymentEnvironmentId.TestEnv,
        _ => false
    };

    private static MigrationGovernanceVerdict Incompatible(
        IReadOnlyList<MigrationGovernanceRefusalReason> reasons,
        IReadOnlyList<string> detail,
        MigrationCompatibility? observedCompatibility)
        => Verdict(MigrationGovernanceDecision.MigrationIncompatible, reasons, observedCompatibility, detail);

    private static MigrationGovernanceVerdict Verdict(
        MigrationGovernanceDecision decision,
        IReadOnlyList<MigrationGovernanceRefusalReason> reasons,
        MigrationCompatibility? observedCompatibility,
        IReadOnlyList<string> detail)
    {
        if (decision is MigrationGovernanceDecision.NoMigrationRequired or MigrationGovernanceDecision.MigrationApproved
            && reasons.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{decision}' permits a deployment and cannot carry refusal reasons. A permitting decision with reasons attached is a bug in this judgement, not a state to be recorded.");
        }

        return MigrationGovernanceVerdict.Of(decision, [.. reasons], observedCompatibility, [.. detail]);
    }
}
