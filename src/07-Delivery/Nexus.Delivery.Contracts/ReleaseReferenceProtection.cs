using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// <b>Which</b> mechanism protects the release-tag namespace. A recorded decision, not a yes/no.
///
/// <para>
/// <b>Why a mode and not a boolean.</b> The member this replaces was a single nullable boolean named
/// <c>ReleaseRefServerSideProtectionVerified</c>, and the Owner-approved C-2 compensating control was
/// recorded in it as <c>true</c> — on an estate where no server-side ruleset exists and none can be
/// installed on these repositories. The value was not a mistake about the mechanism; the boolean was the
/// only member available for saying "the reference is protected". A control that reports itself as the
/// control it replaced is precisely the overstatement the deviation exists to avoid, and it made the
/// release look equally safe in every environment. Two mechanisms with different scopes and different
/// failure modes cannot share one boolean.
/// </para>
///
/// <para>
/// <b>Neither mode is ever inferred from a local observation.</b> A ruleset lives in the remote's settings,
/// so a repository that has one and a repository that has not installed one look identical from a checkout;
/// and the compensating control's status is a governance fact, not a file. Both are supplied by the party
/// entitled to state them and both are recorded as supplied.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseReferenceProtectionMode
{
    /// <summary>
    /// Nothing is claimed. The default, and the honest state until someone records otherwise: a release that
    /// has not said how its reference is protected has not established that it is.
    /// </summary>
    None,

    /// <summary>
    /// The remote repository's own ruleset protects the release-tag namespace, confirmed by an authorized
    /// check. This is the authoritative control the Owner's D2 ruling requires, and it is environment
    /// independent.
    /// </summary>
    ServerSideProtected,

    /// <summary>
    /// The remote's ruleset could not be installed and the Owner-approved C-2 compensating control (Option 4)
    /// stands in its place: the certified chain is the release identity, and the tag is a pointer every
    /// deployment re-derives and checks rather than reads and believes (rules 4–9 of that control).
    ///
    /// <para>
    /// <b>A substitute, never a protection</b>, and available only within the scope recorded beside it. The
    /// judgement below refuses it outside DEV/TEST however it is approved, so the deviation's boundary is
    /// enforced rather than documented.
    /// </para>
    /// </summary>
    GovernedCompensatingControl
}

/// <summary>The environments a recorded protection mechanism is permitted to cover.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseReferenceProtectionScope
{
    /// <summary>No environment is covered. The default.</summary>
    None,

    /// <summary>ENV-DEV and ENV-TEST, the scope the Owner approved for the C-2 deviation. Not ENV-PROD.</summary>
    DevTest,

    /// <summary>
    /// All three ratified environments. A compensating control claiming this scope is <b>refused</b> rather
    /// than narrowed: silently treating it as DEV/TEST would leave the record saying one thing and the
    /// behaviour doing another.
    /// </summary>
    AllRatifiedEnvironments
}

/// <summary>
/// The recorded state of the release reference's protection, carried by whichever evidence bag is in hand.
///
/// <para>
/// <b>One type, so that one judgement serves every stage.</b> The certification gate (which decides DEV
/// readiness) and the deployment gate (which decides a specific environment's transition) ask the same
/// question with different targets, so the question is answered once, here, parameterised by environment.
/// Two stages deriving protection for themselves is how a release becomes deployable at one stage and not at
/// the next for a reason nobody can explain.
/// </para>
/// </summary>
public sealed record ReleaseReferenceProtectionEvidence
{
    public ReleaseReferenceProtectionMode Mode { get; init; } = ReleaseReferenceProtectionMode.None;

    /// <summary>Whether an authorized check observed the remote ruleset. <b>Never inferred locally.</b></summary>
    public bool? ServerSideProtectionVerified { get; init; }

    /// <summary>Whether the Owner approved the C-2 compensating control for this release.</summary>
    public bool? CompensatingControlApproved { get; init; }

    /// <summary>Whether the compensating control's rules were verified against this release's reference.</summary>
    public bool? CompensatingControlVerified { get; init; }

    /// <summary>
    /// The environments the recorded mechanism may cover. Read only by
    /// <see cref="ReleaseReferenceProtectionMode.GovernedCompensatingControl"/>: server-side protection is a
    /// property of the repository and applies wherever the reference is deployed.
    /// </summary>
    public ReleaseReferenceProtectionScope AllowedScope { get; init; } = ReleaseReferenceProtectionScope.None;

    /// <summary>Nothing recorded. The initial state, and the state a caller gets by not answering.</summary>
    public static ReleaseReferenceProtectionEvidence Unrecorded { get; } = new();

    /// <summary>Server-side protection, as an authorized check reported it.</summary>
    public static ReleaseReferenceProtectionEvidence ServerSide(bool? verified = true)
        => new() { Mode = ReleaseReferenceProtectionMode.ServerSideProtected, ServerSideProtectionVerified = verified };

    /// <summary>
    /// The Owner-approved C-2 compensating control. The defaults state the approved case — approved, verified,
    /// and scoped to DEV/TEST — and each may be overridden so a test can break exactly one of them.
    /// </summary>
    public static ReleaseReferenceProtectionEvidence CompensatingControl(
        bool? approved = true,
        bool? verified = true,
        ReleaseReferenceProtectionScope scope = ReleaseReferenceProtectionScope.DevTest)
        => new()
        {
            Mode = ReleaseReferenceProtectionMode.GovernedCompensatingControl,
            CompensatingControlApproved = approved,
            CompensatingControlVerified = verified,
            AllowedScope = scope
        };

    /// <summary>
    /// Whether the recorded mechanism satisfies the requirement <b>for a specific environment</b>.
    ///
    /// <para>
    /// <b>This is the one place that judgement is made.</b> The environment is a parameter rather than a
    /// property of the evidence because the same recorded deviation is sufficient for ENV-DEV and ENV-TEST and
    /// insufficient for ENV-PROD; a judgement that could not see the target would have to either refuse the
    /// DEV deployment it was approved for, or permit the PROD one it was not.
    /// </para>
    ///
    /// <para>
    /// <see cref="ReleaseReferenceProtectionJudgement.IsUnobserved"/> is kept distinct from a plain failure
    /// for the estate's standing reason: "we could not check" and "we checked and it failed" route to
    /// different states, and collapsing them would report a missing observation as a defect in the release.
    /// </para>
    /// </summary>
    public ReleaseReferenceProtectionJudgement Judge(DeploymentEnvironmentId environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        switch (Mode)
        {
            case ReleaseReferenceProtectionMode.ServerSideProtected:
                if (ServerSideProtectionVerified is not true)
                {
                    return ServerSideProtectionVerified is null
                        ? ReleaseReferenceProtectionJudgement.Unrecorded(
                            "the remote ruleset protecting the release-tag namespace has not been observed.")
                        : ReleaseReferenceProtectionJudgement.Outstanding(
                            "the remote ruleset protecting the release-tag namespace is recorded as not yet installed.");
                }

                return ReleaseReferenceProtectionJudgement.Satisfied(
                    "The remote repository's ruleset protects the release-tag namespace, confirmed by an authorized check.");

            case ReleaseReferenceProtectionMode.GovernedCompensatingControl:
                if (CompensatingControlApproved is not true)
                {
                    return CompensatingControlApproved is null
                        ? ReleaseReferenceProtectionJudgement.Unrecorded(
                            "the C-2 compensating control's approval has not been recorded.")
                        : ReleaseReferenceProtectionJudgement.Outstanding(
                            "the C-2 compensating control is recorded as not approved for this release.");
                }

                if (CompensatingControlVerified is not true)
                {
                    return CompensatingControlVerified is null
                        ? ReleaseReferenceProtectionJudgement.Unrecorded(
                            "the C-2 compensating control's rules have not been verified against this release's reference.")
                        : ReleaseReferenceProtectionJudgement.Outstanding(
                            "the C-2 compensating control is recorded as not verified against this release's reference.");
                }

                if (AllowedScope != ReleaseReferenceProtectionScope.DevTest)
                {
                    return ReleaseReferenceProtectionJudgement.Outstanding(
                        $"the C-2 compensating control may replace the ruleset in {DeploymentEnvironmentId.Dev} and "
                        + $"{DeploymentEnvironmentId.Test} only, and is recorded with scope '{AllowedScope}'.");
                }

                if (!IsDevOrTest(environment))
                {
                    return ReleaseReferenceProtectionJudgement.Outstanding(
                        $"the C-2 compensating control is scoped to {DeploymentEnvironmentId.Dev} and "
                        + $"{DeploymentEnvironmentId.Test}, and {environment} is outside that scope. "
                        + "This is not a defect in the release: it is the deviation's boundary.");
                }

                return ReleaseReferenceProtectionJudgement.Satisfied(
                    $"The C-2 compensating control is approved and verified for this release and applied within its "
                    + $"{DeploymentEnvironmentId.Dev}/{DeploymentEnvironmentId.Test} scope. Server-side protection is "
                    + "NOT claimed.");

            default:
                return ReleaseReferenceProtectionJudgement.Unrecorded(
                    "no protection mechanism for the release-tag namespace has been recorded.");
        }
    }

    /// <summary>The ratified DEV/TEST pair, named once. Every other scope test is derived from this one.</summary>
    private static bool IsDevOrTest(DeploymentEnvironmentId environment)
        => environment == DeploymentEnvironmentId.DevEnv || environment == DeploymentEnvironmentId.TestEnv;
}

/// <summary>
/// The answer to "is the release reference protected well enough to proceed here?", with the reason in the
/// terms of the mechanism that was recorded.
/// </summary>
/// <param name="IsSatisfied">True only when the recorded mechanism covers the environment asked about.</param>
/// <param name="IsUnobserved">
/// True when nothing adequate could be established because a required observation is missing — the estate's
/// <c>NOT_RUN</c>, which is never a pass. Mutually exclusive with <paramref name="IsSatisfied"/>.
/// </param>
/// <param name="Detail">
/// Operator-facing, and it states which mechanism is relied on and which condition failed. <b>Never a
/// credential value</b> — this text reaches build and deployment logs.
/// </param>
public sealed record ReleaseReferenceProtectionJudgement(bool IsSatisfied, bool IsUnobserved, string Detail)
{
    /// <summary>The code prefixed to a detail line when a mechanism is missing rather than failing.</summary>
    public const string UnrecordedCode = "RELEASE_REF_PROTECTION_UNRECORDED";

    /// <summary>The code prefixed to a detail line when a recorded mechanism failed.</summary>
    public const string OutstandingCode = "RELEASE_REF_PROTECTION_OUTSTANDING";

    public static ReleaseReferenceProtectionJudgement Satisfied(string detail)
        => new(true, false, detail);

    public static ReleaseReferenceProtectionJudgement Outstanding(string detail)
        => new(false, false, $"{OutstandingCode}: {detail}");

    public static ReleaseReferenceProtectionJudgement Unrecorded(string detail)
        => new(false, true, $"{UnrecordedCode}: {detail}");

    /// <summary>The code this judgement carries, for a caller that lists outstanding actions.</summary>
    public string Code => IsSatisfied
        ? "RELEASE_REF_PROTECTION_SATISFIED"
        : IsUnobserved
            ? UnrecordedCode
            : OutstandingCode;
}
