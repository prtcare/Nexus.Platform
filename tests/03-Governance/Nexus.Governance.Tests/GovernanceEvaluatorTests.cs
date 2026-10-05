using Nexus.Governance.Contracts;
using Nexus.Governance.Core;
using Xunit;

namespace Nexus.Governance.Tests;

/// <summary>
/// W8B — the deterministic governance seam.
///
/// <para>Each test states the branch it is proving rather than only a count, and every refusal test
/// carries a positive twin: a refusal is only evidence when the same request succeeds once the one
/// condition under test is removed. A test that asserts BLOCK and nothing else is satisfied by an
/// evaluator that blocks everything.</para>
/// </summary>
public sealed class GovernanceEvaluatorTests
{
    private static readonly IGovernanceEvaluator Evaluator = new DeterministicGovernanceEvaluator();

    private const string Resource = "D:\\NEXUS\\Platform\\src\\Nexus.ProductCore.Core";

    private static GovernanceRequest Request(
        string caller = "WI-W8B-0001",
        string callerHead = "Forge",
        bool mutating = true,
        string targetHead = "Forge",
        string targetResource = Resource,
        IReadOnlyList<GovernanceScopeItem>? scopeItems = null,
        GovernanceSecurityClassification classification = GovernanceSecurityClassification.Internal,
        string baseSha = "984710f0af02d15dbf81f01bedb9053af9133998",
        IReadOnlyList<string>? unresolved = null)
        => new(
            caller,
            callerHead,
            new GovernanceOperation("forge.protected.mutate", mutating),
            new GovernanceTarget(targetResource, targetHead),
            new GovernanceChangeScope("CHG-W8B-0001", scopeItems ?? [new GovernanceScopeItem(Resource, true)]),
            new GovernanceDependencyContext([], unresolved ?? []),
            classification,
            new GovernanceGitMetadata("Forge", "w8b/forge-v3-adaptation", baseSha));

    // ================================================================ ALLOW

    [Fact]
    public void PermittedOperation_IsAllowed()
    {
        var decision = Evaluator.Evaluate(Request());

        Assert.True(decision.IsAllow, decision.DecidingReason);
        Assert.Equal(GovernanceVerdict.Allow, decision.Verdict);
        Assert.Equal(DeterministicGovernanceEvaluator.Version, decision.EvaluatorVersion);
    }

    [Fact]
    public void PermittedReadOnlyOperation_IsAllowed_SoTheGuardIsNotOverBroad()
    {
        var request = Request(mutating: false, scopeItems: [new GovernanceScopeItem(Resource, false)]);

        Assert.True(Evaluator.Evaluate(request).IsAllow);
    }

    // ================================================================ ownership

    [Fact]
    public void OwnershipViolation_Blocks()
    {
        var decision = Evaluator.Evaluate(Request(callerHead: "Forge", targetHead: "Developer"));

        Assert.True(decision.IsBlock, decision.DecidingReason);
        // The typed token a consumer branches on, not a phrase in prose.
        Assert.Contains("OWNERSHIP_VIOLATION", decision.DecidingReason, StringComparison.Ordinal);
        // ...and the reason is actionable: it names the owning head and what to do about it.
        Assert.Contains("Developer", decision.DecidingReason, StringComparison.Ordinal);
        Assert.Contains("request to that head", decision.DecidingReason, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnershipViolation_Blocks_EvenWhenEverythingElseIsSatisfied()
    {
        // The positive twin above differs in exactly one field, so this proves the block is the
        // ownership rule and not an accident of a malformed request.
        var allowed = Evaluator.Evaluate(Request());
        var blocked = Evaluator.Evaluate(Request(targetHead: "Developer"));

        Assert.True(allowed.IsAllow);
        Assert.True(blocked.IsBlock);
    }

    // ================================================================ ChangeScope

    [Fact]
    public void OutOfScopeMutation_Blocks_WithTheScopeRequiredToken()
    {
        var decision = Evaluator.Evaluate(Request(
            targetResource: "D:\\NEXUS\\Platform\\src\\Nexus.Platform.Contracts",
            scopeItems: [new GovernanceScopeItem(Resource, true)]));

        Assert.True(decision.IsBlock, decision.DecidingReason);
        // The caller is sent to amend its declaration, not to retry.
        Assert.Contains("SCOPE_CHANGE_REQUIRED", decision.DecidingReason, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationAgainstAReadOnlyDeclaration_Blocks_WithTheScopeRequiredToken()
    {
        var decision = Evaluator.Evaluate(Request(
            mutating: true,
            scopeItems: [new GovernanceScopeItem(Resource, write: false)]));

        Assert.True(decision.IsBlock, decision.DecidingReason);
        Assert.Contains("SCOPE_CHANGE_REQUIRED", decision.DecidingReason, StringComparison.Ordinal);
        Assert.Contains("READ-only", decision.DecidingReason, StringComparison.Ordinal);
    }

    // ================================================================ HUMAN_DECISION_REQUIRED

    [Fact]
    public void UnresolvedOwnerDecision_ReturnsHumanDecisionRequired_AndNotBlock()
    {
        // The distinction is the point: a block claims a settled refusal, which this is not.
        var decision = Evaluator.Evaluate(Request(unresolved: ["Nexus.ProductCore.Contracts"]));

        Assert.True(decision.IsHumanDecisionRequired, decision.DecidingReason);
        Assert.False(decision.IsBlock);
        Assert.Contains("could not be resolved", decision.DecidingReason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCallerIdentity_ReturnsHumanDecisionRequired()
    {
        Assert.True(Evaluator.Evaluate(Request(caller: "")).IsHumanDecisionRequired);
    }

    [Fact]
    public void MutatingOperationWithoutABaseSha_ReturnsHumanDecisionRequired()
    {
        var decision = Evaluator.Evaluate(Request(baseSha: ""));

        Assert.True(decision.IsHumanDecisionRequired, decision.DecidingReason);
        Assert.Contains("rollback point", decision.DecidingReason, StringComparison.Ordinal);

        // ...and the same request WITH a BaseSHA is allowed, so the rule is not a blanket refusal.
        Assert.True(Evaluator.Evaluate(Request()).IsAllow);
    }

    [Fact]
    public void SecretClassifiedTarget_IsNeverAllowedByAnAutomatedVerdict()
    {
        var decision = Evaluator.Evaluate(Request(classification: GovernanceSecurityClassification.Secret));

        Assert.False(decision.IsAllow);
        Assert.True(decision.IsHumanDecisionRequired, decision.DecidingReason);
        Assert.Contains("Secret", decision.DecidingReason, StringComparison.Ordinal);
    }

    // ================================================================ ordering

    [Fact]
    public void ARefusalOutranksAnEscalation_WhenBothFire()
    {
        // Both an ownership violation (BLOCK) and an unresolved dependency (HUMAN_DECISION_REQUIRED)
        // are present. A refusal is the actionable answer, so BLOCK must win.
        var decision = Evaluator.Evaluate(Request(targetHead: "Developer", unresolved: ["x"]));

        Assert.True(decision.IsBlock, decision.DecidingReason);
        // ...and nothing is hidden: both findings are reported, deciding reason first.
        Assert.Equal(2, decision.Reasons.Count);
    }

    [Fact]
    public void EveryReasonIsReported_NotOnlyTheFirst()
    {
        var decision = Evaluator.Evaluate(Request(baseSha: "", unresolved: ["a", "b"]));

        Assert.Equal(2, decision.Reasons.Count);
        Assert.Contains(decision.Reasons, r => r.Contains("rollback point", StringComparison.Ordinal));
        Assert.Contains(decision.Reasons, r => r.Contains("could not be resolved", StringComparison.Ordinal));
    }

    // ================================================================ determinism

    [Fact]
    public void TheSameRequestAlwaysYieldsTheSameDecision()
    {
        var request = Request();

        var first = Evaluator.Evaluate(request);
        var second = Evaluator.Evaluate(request);
        var third = Evaluator.Evaluate(Request());

        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.Reasons, second.Reasons);
        Assert.Equal(first.Verdict, third.Verdict);
        Assert.Equal(first.Reasons, third.Reasons);
    }

    [Fact]
    public void EvaluationReadsNoClock_SoTwoRequestsBuiltAtDifferentTimesAgree()
    {
        // Nothing in Evaluate consults a clock; this pins that the verdict does not drift with time
        // by building two structurally identical requests from separate constructions.
        var a = Evaluator.Evaluate(Request());
        Thread.Sleep(5);
        var b = Evaluator.Evaluate(Request());

        Assert.Equal(a.Verdict, b.Verdict);
        Assert.Equal(a.DecidingReason, b.DecidingReason);
    }
}
