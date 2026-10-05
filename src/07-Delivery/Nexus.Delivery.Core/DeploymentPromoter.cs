using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Orchestrates one transition: decide, then record, in that order, with no step skipped.
///
/// <para>
/// <b>Why this is the only place a transition completes.</b> Invariant I-5 says every transition writes
/// a lineage record <i>before it is considered complete</i>. Making that a convention means the first
/// hurried caller skips it, and the estate already holds a permanent example of the cost — Scenario A's
/// original run took no pre-write backup, so its authority transition is evidenced structurally rather
/// than by a byte diff (W8-DEBT-09, recorded as permanent). Here the ordering is not a convention: this
/// type will not report completion unless the ledger accepted the record.
/// </para>
///
/// <para>
/// <b>It does not touch environments.</b> There is no deployment here and no infrastructure — this type
/// decides and records. That separation is what lets the decision be pure and the recording be
/// replaceable (a file today, the governed workbook in W9.2) without the gate changing.
/// </para>
/// </summary>
public sealed class DeploymentPromoter
{
    private readonly IArtifactRegistry _registry;
    private readonly IDeploymentLineageLog _lineage;

    public DeploymentPromoter(IArtifactRegistry registry, IDeploymentLineageLog lineage)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _lineage = lineage ?? throw new ArgumentNullException(nameof(lineage));
    }

    /// <summary>
    /// Evaluates <paramref name="request"/> and, when allowed, records it.
    ///
    /// <para>
    /// <paramref name="lineageId"/> is supplied by the caller rather than generated here. A generated
    /// id would make the record's identity depend on when it happened, which is exactly the property
    /// that makes a re-run produce a second, contradictory record instead of a refusal. The caller —
    /// a pipeline, holding a run counter — is the only party that can number a series deterministically.
    /// </para>
    /// </summary>
    /// <param name="deploymentId">
    /// <b>The identity of the deployment attempt.</b> Required, and non-nullable on purpose: <c>LineageId</c>
    /// numbers an audit record, <c>DeploymentId</c> names a deployment, and the W9.4 re-establishment proved
    /// the estate cannot use one for the other. A caller that cannot say which deployment it is recording is
    /// a caller that does not know, and this refuses to guess.
    /// </param>
    /// <param name="previousDeploymentId">
    /// The attempt this one follows, when it is a recovery, a re-deployment or a re-verification rather than
    /// the first. Optional because a first attempt genuinely has none — and an attempt that wrongly claims a
    /// predecessor is caught by the same ledger read that catches a duplicate <c>LineageId</c>.
    /// </param>
    public async Task<PromotionOutcome> TransitionAsync(
        PromotionRequest request,
        string lineageId,
        DeploymentId deploymentId,
        DeploymentId? previousDeploymentId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(deploymentId);

        // The id must name the environment the transition actually targets. A deployment id is deterministic
        // and therefore easy to construct correctly, which is exactly why a wrong one would never be noticed
        // by reading it.
        if (deploymentId.Environment != request.TargetEnvironment)
        {
            throw new ArgumentException(
                $"The deployment id '{deploymentId.Value}' names {deploymentId.Environment}, but this transition targets "
                + $"{request.TargetEnvironment}. An attempt identity that disagrees with the attempt is worse than none.",
                nameof(deploymentId));
        }

        var decision = DeploymentStateMachine.Decide(request);

        if (decision.IsRefused)
        {
            return PromotionOutcome.Refused(decision);
        }

        var record = new DeploymentLineageRecord(
            lineageId,
            DateTimeOffset.UtcNow,
            request.Transition,
            request.Bundle.BundleId,
            decision.FromState,
            decision.NextState,
            request.TargetEnvironment,
            request.Bundle.Source.CommitSha,
            [.. request.Bundle.AllArtifacts],
            request.Bundle.Migrations.FirstOrDefault(m => m.UnitId == request.Bundle.Artifacts[0].UnitId)?.Migrations
                ?? MigrationMetadata.None,
            request.Evidence.Authorization,
            request.Evidence.Reason,
            request.Evidence.PreviousBundle?.BundleId,
            [.. request.Bundle.ConfigurationKeys.SelectMany(kvp => kvp.Value).Distinct().OrderBy(k => k, StringComparer.Ordinal)],
            [.. request.Bundle.SecretReferences.SelectMany(kvp => kvp.Value).Distinct().OrderBy(s => s.Value, StringComparer.Ordinal)],
            runtimeConsumptionObserved: null,
            deploymentId: deploymentId,
            previousDeploymentId: previousDeploymentId);

        var append = await _lineage.AppendAsync(record, cancellationToken).ConfigureAwait(false);

        if (!append.IsAppended)
        {
            return PromotionOutcome.RecordedButIncomplete(decision, record, append.RefusalDetail ?? "The lineage ledger refused the record.");
        }

        // Quarantine is the one transition with a side effect in the registry, because a quarantine
        // that exists only in the ledger would leave the bundle promotable by anyone reading the
        // registry. Applied AFTER the record, so the decision is durable before it takes effect.
        if (request.Transition == DeploymentTransition.Quarantine)
        {
            await _registry.QuarantineAsync(
                request.Bundle.BundleId,
                request.Evidence.Reason ?? "Quarantined without a stated reason.",
                cancellationToken).ConfigureAwait(false);
        }

        return PromotionOutcome.Completed(decision, record);
    }
}
