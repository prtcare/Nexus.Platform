namespace Nexus.Delivery.Contracts;

/// <summary>
/// The result of attempting a transition through the promoter: the machine's decision, and whether the
/// transition was completed.
///
/// <para>
/// <b>A transition is not complete until its lineage record is appended</b> (invariant I-5). This
/// outcome keeps those two facts separate — <see cref="Decision"/> may be an allowance while
/// <see cref="IsComplete"/> is false, because the gate passed and the ledger refused. Reporting that as
/// success would be the same defect the estate keeps re-finding in another form: a check that appeared
/// to pass because the thing that would have contradicted it was never run.
/// </para>
/// </summary>
public sealed record PromotionOutcome(
    DeploymentDecision Decision,
    DeploymentLineageRecord? LineageRecord,
    bool IsComplete,
    string? Detail)
{
    public bool IsAllowed => Decision.IsAllowed;

    public static PromotionOutcome Refused(DeploymentDecision decision)
        => new(decision, null, false, decision.ToString());

    public static PromotionOutcome Completed(DeploymentDecision decision, DeploymentLineageRecord record)
        => new(decision, record, true, $"{record.LineageId} recorded");

    public static PromotionOutcome RecordedButIncomplete(DeploymentDecision decision, DeploymentLineageRecord record, string detail)
        => new(decision, record, false, detail);
}
