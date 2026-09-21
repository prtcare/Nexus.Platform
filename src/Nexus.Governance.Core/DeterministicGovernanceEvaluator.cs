using Nexus.Governance.Contracts;

namespace Nexus.Governance.Core;

/// <summary>
/// The deterministic L03 GOVERNANCE evaluator.
///
/// <para><b>What "deterministic" buys, stated as the property it is.</b> <see cref="Evaluate"/> is
/// a pure function of its argument. It reads no clock, performs no I/O, consults no store, holds no
/// mutable state and touches no ambient configuration. The same <see cref="GovernanceRequest"/>
/// therefore yields the same <see cref="GovernanceDecision"/> on every host, in every process, in
/// any order — which is the only basis on which a verdict can be an authority rather than an
/// opinion.</para>
///
/// <para><b>The rule order is the contract.</b> Rules run in the fixed sequence below and the first
/// one that decides sets the verdict; later rules still contribute reasons, so a caller sees every
/// finding rather than only the first. Ordering is by consequence, not by convenience: an operation
/// nobody can be identified as performing is undecidable before it is unauthorised; an operation
/// outside its declaration is refused before the target's classification is considered, because
/// "you may not touch this" is the actionable answer and "this is secret" is not a remedy.</para>
///
/// <para><b>Where AI is deliberately absent.</b> This type has no model, provider, gateway,
/// confidence or recommendation input, and <see cref="GovernanceRequest"/> has nowhere to carry
/// one. AI evidence cannot influence a verdict here because it cannot reach the function. That is a
/// structural property, not a policy this code promises to honour.</para>
/// </summary>
public sealed class DeterministicGovernanceEvaluator : IGovernanceEvaluator
{
    /// <summary>Bump when the rules change. Stored verdicts stay interpretable against it.</summary>
    public const string Version = "v3-governance-1";

    public string EvaluatorVersion => Version;

    public GovernanceDecision Evaluate(GovernanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Findings are collected into two typed lists rather than one list of prefixed strings.
        // The prefix-parsing version of this evaluator was a real defect caught by its own tests:
        // the scope refusal is reported under the token the CONSUMER branches on
        // (`SCOPE_CHANGE_REQUIRED`), not under `BLOCK`, so a rule keyed off the `BLOCK:` prefix
        // silently classified an out-of-scope mutation as an escalation. Consequence outranks
        // spelling, so the lists decide and the prose is only prose.
        var refusals = new List<string>();
        var undecided = new List<string>();

        // ---- RULE 1: identifiability. An operation attributed to nobody, or addressing nothing,
        // is not decidable — and reporting it as BLOCK would claim a refusal the rules never made.
        if (string.IsNullOrWhiteSpace(request.Caller))
        {
            undecided.Add("UNDECIDABLE: no caller or work item was supplied, so no operation can be attributed.");
        }

        if (string.IsNullOrWhiteSpace(request.CallerHead))
        {
            undecided.Add("UNDECIDABLE: the caller declared no head, so there is nothing to check ownership against.");
        }

        if (undecided.Count > 0)
        {
            return Decide(GovernanceVerdict.HumanDecisionRequired, undecided);
        }

        // ---- RULE 2: ownership. The target declares who owns it; the caller declares who it acts
        // for. A mismatch is a refusal, not an escalation: the remedy is a request to the owning
        // head, which is a thing the caller can do.
        if (!string.Equals(request.Target.Head, request.CallerHead, StringComparison.Ordinal))
        {
            refusals.Add(
                "OWNERSHIP_VIOLATION: '" + request.Target.Resource + "' is owned by head '"
                + request.Target.Head + "', but the caller acts for head '" + request.CallerHead
                + "'. A change to another head's resource requires a request to that head.");
        }

        // ---- RULE 3: ChangeScope. The declaration is what makes a mutation legitimate; a target
        // the declaration does not name is refused with the scope-required token, so the caller is
        // sent to amend its declaration rather than to guess.
        var covering = request.ChangeScope.Items
            .Where(i => string.Equals(i.Target, request.Target.Resource, StringComparison.Ordinal))
            .ToArray();

        if (covering.Length == 0)
        {
            refusals.Add(
                "SCOPE_CHANGE_REQUIRED: '" + request.Target.Resource
                + "' is not covered by the declared scope of change '" + request.ChangeScope.ChangeId
                + "'. Amend the ChangeScope before attempting this operation.");
        }
        else if (request.Operation.Mutating && !covering.Any(i => i.Write))
        {
            refusals.Add(
                "SCOPE_CHANGE_REQUIRED: '" + request.Target.Resource
                + "' is declared READ-only in change '" + request.ChangeScope.ChangeId
                + "', but the operation mutates it. Amend the declaration to WRITE.");
        }

        // ---- RULE 4: security classification. A secret-classified target is never authorised by
        // an automated verdict. This is deliberately the one place the evaluator will not decide
        // for itself: the estate's standing position is that secret material is not read, carried
        // or migrated by automation, so "may an automated change touch this" is an Owner question
        // rather than a rule question. Reached only after ownership and scope, so a caller whose
        // scope is wrong is told that instead of being told the target is secret.
        if (request.SecurityClassification == GovernanceSecurityClassification.Secret)
        {
            undecided.Add(
                "SECRET_CLASSIFIED: '" + request.Target.Resource
                + "' is classified Secret, and no automated verdict authorises a change to secret"
                + " material. No value was read, carried or compared in reaching this verdict.");
        }

        // ---- RULE 5: Git/change metadata. A mutating operation that cannot state its BaseSHA
        // cannot be rolled back, and the W8D forward-lineage rule makes that metadata mandatory for
        // every newly created or modified governed work item.
        if (request.Operation.Mutating && !request.GitMetadata.IsWellFormed)
        {
            undecided.Add(
                "INCOMPLETE_LINEAGE: the operation mutates '" + request.Target.Resource
                + "' but carries incomplete Git/change metadata (repository, branch and BaseSHA are"
                + " all required). Without a BaseSHA the operation has no rollback point.");
        }

        // ---- RULE 6: dependency context. An unresolved dependency is undecidable, not refused:
        // the caller may be able to resolve it, and telling it so is more useful than a BLOCK it
        // cannot act on.
        if (request.DependencyContext.Unresolved.Count > 0)
        {
            undecided.Add(
                "UNRESOLVED_DEPENDENCY: " + request.DependencyContext.Unresolved.Count
                + " dependency target(s) could not be resolved ("
                + string.Join(", ", request.DependencyContext.Unresolved.Take(5))
                + "). The operation cannot be evaluated until the context is complete.");
        }

        if (refusals.Count == 0 && undecided.Count == 0)
        {
            return Decide(
                GovernanceVerdict.Allow,
                ["ALLOW: ownership, ChangeScope, classification, Git metadata and dependency context all check out."]);
        }

        // A refusal outranks an escalation: if any rule refused, the operation is refused. Only when
        // nothing refused and something is undecided does the caller get HumanDecisionRequired.
        // Refusals are reported first, so the DECIDING reason is the actionable one.
        if (refusals.Count > 0)
        {
            return Decide(GovernanceVerdict.Block, [.. refusals, .. undecided]);
        }

        return Decide(GovernanceVerdict.HumanDecisionRequired, undecided);
    }

    private static GovernanceDecision Decide(GovernanceVerdict verdict, IReadOnlyList<string> reasons) =>
        new(verdict, reasons, Version);
}
