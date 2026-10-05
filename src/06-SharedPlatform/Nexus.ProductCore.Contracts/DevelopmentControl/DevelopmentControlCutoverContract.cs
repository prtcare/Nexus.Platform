namespace Nexus.ProductCore.Contracts.DevelopmentControl;

/// <summary>
/// W8D FINAL TASK 3. Why a cutover was or was not performed.
///
/// <para>Every refusal is typed and named, for the same reason
/// <see cref="DevelopmentControlWriteVerdict"/> is: the caller's remedy differs by case. "This is
/// already authoritative" is a no-op that a re-run should report as success-shaped; "this site
/// holds a value I did not expect" is a workbook that has changed underneath the operator and must
/// be re-read; "structural integrity failed" is a candidate that must not be promoted at all.</para>
/// </summary>
public enum DevelopmentControlCutoverVerdict
{
    Unknown = 0,

    /// <summary>Performed: every site moved from the declared before-state to the after-state.</summary>
    Performed = 1,

    /// <summary>Refused: every site already reads the after-state. Nothing to do.</summary>
    RefusedAlreadyAuthoritative = 2,

    /// <summary>Refused: the workbook does not read as the V3 model.</summary>
    RefusedNotV3 = 3,

    /// <summary>Refused: the V3 schema identity or version does not match this build.</summary>
    RefusedSchemaMismatch = 4,

    /// <summary>Refused: the authority marker is not readable, so the before-state cannot be established.</summary>
    RefusedMarkerUnreadable = 5,

    /// <summary>Refused: a declared site is absent from the workbook, or its row/column is gone.</summary>
    RefusedSiteMissing = 6,

    /// <summary>Refused: a site holds a value that is neither the declared before-state nor the after-state.</summary>
    RefusedSiteValueUnexpected = 7,

    /// <summary>Refused: structural integrity did not pass. A candidate that is not whole must not be promoted.</summary>
    RefusedStructuralIntegrity = 8,

    /// <summary>Refused: the path is a refused class — a preserved legacy revision, or outside every governed root.</summary>
    RefusedPath = 9,

    /// <summary>Refused: the reservation is released or its lease has expired.</summary>
    RefusedNoReservation = 10,

    /// <summary>Refused: the caller's H-1E attestation is incomplete, so a precondition is unproven.</summary>
    RefusedAttestationIncomplete = 11,

    /// <summary>Refused: the write landed but read-back did not reproduce the after-state.</summary>
    RefusedReadBackMismatch = 12,
}

/// <summary>
/// W8D FINAL TASK 3, Owner decision H-1E. The two preconditions the component cannot test for
/// itself, required as an explicit attestation rather than assumed.
///
/// <para><b>Why an attestation and not a check.</b> H-1E makes cutover conditional on "shared
/// writer/reader/lock tests are green" and "Forge and Developer interpret the same canonical data
/// identically". The component cannot run two hosts' test suites from inside a workbook write, and
/// a cutover that silently treated those two conditions as satisfied would be asserting something
/// it never established — which is the same class of error as the marker check it replaced. So the
/// operator must state them, the component records that they were stated, and the evidence carries
/// the reference.</para>
///
/// <para>Both must be true. There is no overload that defaults them, because a default of
/// <c>true</c> would make the attestation decorative and a default of <c>false</c> would make the
/// operation unreachable.</para>
/// </summary>
public sealed record DevelopmentControlCutoverAttestation(
    bool SharedContractSuitesGreen,
    bool BothHostsInterpretIdentically,
    string EvidenceRef)
{
    public bool IsComplete => SharedContractSuitesGreen && BothHostsInterpretIdentically
        && !string.IsNullOrWhiteSpace(EvidenceRef);
}

/// <summary>
/// The cutover request. <paramref name="Rationale"/> is required and is recorded, because the
/// cutover is the one operation in this component that changes what the estate considers
/// authoritative, and "why" is part of the governed record rather than a comment in a script.
/// </summary>
public sealed record DevelopmentControlCutoverRequest(
    string ChangeId,
    string Rationale,
    DevelopmentControlCutoverAttestation Attestation)
{
    /// <summary>The state a cutover moves FROM. Not caller-selectable in the governed path.</summary>
    public string FromState { get; init; } = DevelopmentControlAuthoritySites.Candidate;

    /// <summary>The state a cutover moves TO. Not caller-selectable in the governed path.</summary>
    public string ToState { get; init; } = DevelopmentControlAuthoritySites.Authoritative;
}

/// <summary>One site's before and after value, so the evidence names what moved rather than counting it.</summary>
public sealed record DevelopmentControlCutoverSiteChange(
    string Role,
    string Reference,
    string Before,
    string After);

/// <summary>
/// The cutover outcome. Carries the hashes on both sides because a cutover is the moment the
/// artifact's identity changes, and "which bytes are authoritative" must be answerable from the
/// result alone rather than by re-hashing afterwards and hoping nothing else moved in between.
/// </summary>
public sealed record DevelopmentControlCutoverResult(
    bool Performed,
    DevelopmentControlCutoverVerdict Verdict,
    string Reason,
    string Path,
    string Sha256Before,
    string Sha256After,
    string StateBefore,
    string StateAfter,
    IReadOnlyList<DevelopmentControlCutoverSiteChange> Sites,
    int UnresolvedDecisionCount,
    string StructuralIntegrityReport,
    string ChangeId,
    string Rationale,
    string EvidenceRef)
{
    /// <summary>Every site's role, for a one-line transcript.</summary>
    public string SiteSummary => string.Join(", ", Sites.Select(s => $"{s.Reference}:{s.Before}->{s.After}"));
}
