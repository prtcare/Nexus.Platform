namespace Nexus.Delivery.Contracts;

/// <summary>
/// A request to roll an environment back to the bundle it held before.
///
/// <para>
/// <b>Rollback reverts the artifact, never the data.</b> A deployment can be undone in seconds; a
/// schema change or a written row cannot. This request therefore carries
/// <see cref="CrossesMigrationBoundary"/> explicitly, because that single fact is the difference
/// between an operation and an incident: rolling an artifact back across a migration boundary runs
/// the <i>new</i> schema with the <i>old</i> code, which is safe only when the migration was additive.
/// </para>
///
/// <para>
/// And <see cref="Rehearsed"/> is a member rather than a process note, because an unrehearsed rollback
/// plan is a document, not a capability (rule R-7.1).
/// </para>
/// </summary>
public sealed record RollbackRequest(
    BundleId CurrentBundleId,
    BundleId? PreviousBundleId,
    DeploymentEnvironmentId TargetEnvironment,
    bool PreviousBundleAvailable,
    bool CrossesMigrationBoundary,
    bool Rehearsed,
    DeploymentAuthorization? Authorization = null,
    string? Reason = null);

/// <summary>The outcome of planning a rollback.</summary>
public sealed record RollbackPlan
{
    private RollbackPlan(bool isPermitted, IReadOnlyList<DeploymentRefusalReason> refusalReasons, string summary)
    {
        IsPermitted = isPermitted;
        RefusalReasons = refusalReasons;
        Summary = summary;
    }

    public bool IsPermitted { get; }

    public IReadOnlyList<DeploymentRefusalReason> RefusalReasons { get; }

    /// <summary>Operator-facing summary. Must never contain a secret value.</summary>
    public string Summary { get; }

    public static RollbackPlan Permit(BundleId previous, DeploymentEnvironmentId environment)
        => new(true, [], $"Roll back {environment} to {previous}.");

    public static RollbackPlan Refuse(params DeploymentRefusalReason[] reasons)
        => new(false, reasons.Distinct().OrderBy(r => r).ToArray(), "Rollback refused.");

    /// <summary>
    /// Plans a rollback.
    ///
    /// <para>
    /// Two refusals are worth stating in advance because they are the ones that will actually fire.
    /// <b>A cross-migration rollback requires an Owner authorization</b>: no <c>Down</c> migration has
    /// ever been exercised by any suite in this estate (W9.0 F-4.4), so the operation is genuinely
    /// irreversible and must be Owner-authorized, backed up and rehearsed. And <b>an unavailable
    /// previous bundle refuses</b>: if retention did not keep it, the rollback was never a capability
    /// — which is why the registry interface carries <c>TryOpenAsync</c> and W9.1's first proof
    /// rehearses the rollback rather than asserting it.
    /// </para>
    /// </summary>
    public static RollbackPlan Plan(RollbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reasons = new List<DeploymentRefusalReason>();

        if (request.PreviousBundleId is null || !request.PreviousBundleAvailable)
        {
            reasons.Add(DeploymentRefusalReason.PreviousBundleUnavailable);
        }

        if (request.CrossesMigrationBoundary)
        {
            if (!request.Rehearsed)
            {
                reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
            }

            if (request.Authorization?.IsOwnerAuthorized != true)
            {
                reasons.Add(DeploymentRefusalReason.OwnerAuthorizationRequired);
            }
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            reasons.Add(DeploymentRefusalReason.EvidenceIncomplete);
        }

        if (reasons.Count > 0)
        {
            return Refuse([.. reasons]);
        }

        return Permit(request.PreviousBundleId!, request.TargetEnvironment);
    }
}
