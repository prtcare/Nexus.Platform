namespace Nexus.Delivery.Contracts;

/// <summary>
/// Append-only access to the deployment lineage ledger.
///
/// <para>
/// <b>Append-only, and correctable only by supersession.</b> Invariant L-3: a correction is a new
/// record that names the one it supersedes — never an edit. The estate already holds a permanent
/// example of why: Scenario A's committed evidence file carries a wrong <c>tests</c> field that
/// cannot be corrected without a history rewrite (W8-DEBT-08), and it was superseded by a
/// supplementary proof instead. The lesson generalised into this signature: there is no update
/// method, and there is no delete.
/// </para>
///
/// <para>
/// <b>Adapter note.</b> This interface is implemented in <c>Nexus.Delivery.Core</c> by a deterministic
/// file-backed log for W9.1. The <b>governed</b> implementation — writing into the authority
/// workbook's <c>13_GitLineage</c> sheet through the shared DevelopmentControl component, under a
/// reservation, with the authority pin re-certified afterwards — is a W9.2 deliverable, and
/// <c>DEPLOYMENT_LINEAGE_MODEL.md</c> §1 requires the reader/writer state to be re-verified before
/// that work starts rather than assumed green from memory.
/// </para>
/// </summary>
public interface IDeploymentLineageLog
{
    /// <summary>
    /// Appends a record. Must refuse a duplicate <see cref="DeploymentLineageRecord.LineageId"/> —
    /// the append path refuses duplicates on immutable record identity, which is the same discipline
    /// W8E's workbook append uses.
    /// </summary>
    Task<LineageAppendOutcome> AppendAsync(DeploymentLineageRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentLineageRecord>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task<DeploymentLineageRecord?> TryGetAsync(string lineageId, CancellationToken cancellationToken = default);

    /// <summary>Records by bundle, oldest first. How a promoter asks "what has this bundle already done?"</summary>
    Task<IReadOnlyList<DeploymentLineageRecord>> ReadByBundleAsync(BundleId bundleId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of an append. A duplicate is an ordinary refusal, not an exception.</summary>
public sealed record LineageAppendOutcome(bool IsAppended, string? RefusalDetail)
{
    public static LineageAppendOutcome Appended() => new(true, null);

    public static LineageAppendOutcome Refused(string detail) => new(false, detail);
}
