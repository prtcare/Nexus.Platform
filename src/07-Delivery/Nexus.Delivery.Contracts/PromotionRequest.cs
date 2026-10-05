namespace Nexus.Delivery.Contracts;

/// <summary>A request to move a bundle through one transition, with everything the gate needs to decide.</summary>
public sealed record PromotionRequest
{
    public PromotionRequest(
        ReleaseBundle bundle,
        DeploymentEnvironmentId targetEnvironment,
        PromotionState currentState,
        DeploymentTransition transition,
        DeploymentGateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(targetEnvironment);
        ArgumentNullException.ThrowIfNull(evidence);

        Bundle = bundle;
        TargetEnvironment = targetEnvironment;
        CurrentState = currentState;
        Transition = transition;
        Evidence = evidence;
    }

    public ReleaseBundle Bundle { get; }

    public DeploymentEnvironmentId TargetEnvironment { get; }

    public PromotionState CurrentState { get; }

    public DeploymentTransition Transition { get; }

    public DeploymentGateEvidence Evidence { get; }

    /// <summary>Units in this bundle that are marked excluded from deployment, if any. Any presence refuses the promotion.</summary>
    public IReadOnlyList<DeploymentUnitId> ExcludedUnitsInBundle { get; init; } = [];

    /// <summary>Set when the registry already holds a bundle under this id, which makes the id unusable for a new release.</summary>
    public bool BundleIdAlreadyRegistered { get; init; }
}
