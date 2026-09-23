namespace Nexus.Delivery.Contracts;

/// <summary>
/// The release lifecycle machine's answer: the transition, the state it was asked from, the state it lands
/// in, and — when it refuses — the typed reasons.
///
/// <para>
/// Mirrors <see cref="DeploymentDecision"/> deliberately. Two machines in one estate that reported their
/// answers in different shapes would each need their own reader, and the reader is where a refusal reason
/// gets lost.
/// </para>
/// </summary>
public sealed record ReleaseDecision
{
    private ReleaseDecision(
        bool isAllowed,
        ReleaseTransition transition,
        ReleaseLifecycleState fromState,
        ReleaseLifecycleState? toState,
        IReadOnlyList<ReleaseRefusalReason> refusalReasons)
    {
        IsAllowed = isAllowed;
        Transition = transition;
        FromState = fromState;
        ToState = toState;
        RefusalReasons = refusalReasons;
    }

    public bool IsAllowed { get; }

    public ReleaseTransition Transition { get; }

    public ReleaseLifecycleState FromState { get; }

    /// <summary>Non-null exactly when <see cref="IsAllowed"/>. A refusal does not name a destination.</summary>
    public ReleaseLifecycleState? ToState { get; }

    public IReadOnlyList<ReleaseRefusalReason> RefusalReasons { get; }

    public bool RefusedBecause(ReleaseRefusalReason reason) => RefusalReasons.Contains(reason);

    public static ReleaseDecision Allow(ReleaseTransition transition, ReleaseLifecycleState from, ReleaseLifecycleState to)
        => new(true, transition, from, to, []);

    public static ReleaseDecision Refuse(
        ReleaseTransition transition,
        ReleaseLifecycleState from,
        params ReleaseRefusalReason[] reasons)
    {
        if (reasons is null || reasons.Length == 0)
        {
            throw new ArgumentException(
                "A refusal must name at least one typed reason. An unnamed refusal is indistinguishable from a bug.",
                nameof(reasons));
        }

        return new ReleaseDecision(false, transition, from, null, [.. reasons.Distinct().OrderBy(r => r)]);
    }

    public override string ToString() => IsAllowed
        ? $"{Transition}: {FromState} → {ToState}"
        : $"{Transition}: REFUSED [{string.Join(", ", RefusalReasons)}]";
}
