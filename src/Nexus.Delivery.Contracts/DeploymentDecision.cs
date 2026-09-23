namespace Nexus.Delivery.Contracts;

/// <summary>
/// The outcome of asking the state machine whether a transition may proceed.
///
/// <para>
/// A refusal is not an exception: it is an ordinary, expected, recorded result. The estate's own
/// history is the reason — in W8E, six of seven no-bypass controls refused by mechanism and the
/// value of that was that each refusal could be asserted on by name. A gate that throws tells the
/// caller nothing it can act on.
/// </para>
/// </summary>
public sealed record DeploymentDecision
{
    private DeploymentDecision(
        bool isAllowed,
        DeploymentTransition transition,
        PromotionState fromState,
        PromotionState nextState,
        IReadOnlyList<DeploymentRefusalReason> refusalReasons)
    {
        IsAllowed = isAllowed;
        Transition = transition;
        FromState = fromState;
        NextState = nextState;
        RefusalReasons = refusalReasons;
    }

    public bool IsAllowed { get; }

    public DeploymentTransition Transition { get; }

    public PromotionState FromState { get; }

    /// <summary>Equal to <see cref="FromState"/> when refused.</summary>
    public PromotionState NextState { get; }

    public IReadOnlyList<DeploymentRefusalReason> RefusalReasons { get; }

    public bool IsRefused => !IsAllowed;

    public static DeploymentDecision Allow(
        DeploymentTransition transition,
        PromotionState fromState,
        PromotionState nextState)
        => new(true, transition, fromState, nextState, []);

    public static DeploymentDecision Refuse(
        DeploymentTransition transition,
        PromotionState fromState,
        params DeploymentRefusalReason[] reasons)
    {
        if (reasons is null || reasons.Length == 0)
        {
            throw new ArgumentException(
                "A refusal must name at least one typed reason. An unnamed refusal is indistinguishable from a bug.",
                nameof(reasons));
        }

        var distinct = reasons.Distinct().OrderBy(r => r).ToArray();
        return new(false, transition, fromState, fromState, distinct);
    }

    public bool RefusedBecause(DeploymentRefusalReason reason) => RefusalReasons.Contains(reason);

    public override string ToString() => IsAllowed
        ? $"{Transition}: {FromState} -> {NextState} ALLOWED"
        : $"{Transition}: REFUSED at {FromState} [{string.Join(", ", RefusalReasons)}]";
}
