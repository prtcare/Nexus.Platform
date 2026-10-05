namespace Nexus.Governance.Contracts;

/// <summary>
/// L03 GOVERNANCE verdict for a protected operation.
///
/// <para><b>Three values, and the third is not a failure.</b> <see cref="Allow"/> and
/// <see cref="Block"/> are decisions the deterministic evaluator can reach on its own.
/// <see cref="HumanDecisionRequired"/> is the answer when the inputs do not determine an outcome —
/// an unresolved owner decision, a dependency that cannot be resolved from the supplied context —
/// and collapsing it into <see cref="Block"/> would report a settled refusal where none exists,
/// while collapsing it into <see cref="Allow"/> would let an undecided case proceed.</para>
///
/// <para><b>These are verdict NAMES, not magnitudes.</b> Nothing compares them ordinally; a caller
/// branches on equality. Adding a fourth value is a contract change, not a ranking change.</para>
/// </summary>
public enum GovernanceVerdict
{
    /// <summary>The operation is permitted by the deterministic rules.</summary>
    Allow,

    /// <summary>The operation is refused by the deterministic rules.</summary>
    Block,

    /// <summary>
    /// The rules cannot decide from the supplied inputs. The operation must not proceed on this
    /// verdict alone, and it must not be reported as blocked either.
    /// </summary>
    HumanDecisionRequired,
}
