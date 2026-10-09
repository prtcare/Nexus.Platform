using Nexus.Platform.Contracts.Identity;
using Xunit;

namespace Nexus.Platform.Identity.Tests;

/// <summary>A policy that always refuses, for the negative cases.</summary>
internal sealed class RefusingPolicy : INexusAuthorizationPolicy
{
    public string Authority => "test.refusing";

    public AuthorizationDecision Authorize(NexusActor actor, string action, string target, string? scope)
        => new() { Outcome = AuthorizationOutcome.Refused, Reason = "Refused by the test policy.", DecidingAuthority = Authority };
}

/// <summary>A policy that always authorizes, so governance can be exercised on its own.</summary>
internal sealed class AllowingPolicy : INexusAuthorizationPolicy
{
    public string Authority => "test.allowing";

    public AuthorizationDecision Authorize(NexusActor actor, string action, string target, string? scope)
        => new() { Outcome = AuthorizationOutcome.Authorized, Reason = "Authorized by the test policy.", DecidingAuthority = Authority };
}

/// <summary>
/// A governance gate. Separate from authorization on purpose — the two answer different questions and
/// neither implies the other.
/// </summary>
internal interface ITestGovernanceGate
{
    bool Allow { get; }

    /// <summary>Whether governance permits the operation. A refusal, not an error.</summary>
    bool Decides(NexusActor actor, string action);
}

/// <summary>
/// The controlled operation — the only thing that has a side effect, and the thing the ordering proof is
/// about. It is not a real W10.8 action and is exposed to nothing.
/// </summary>
internal sealed class ControlledHandler
{
    public int Invocations { get; private set; }

    public void Handle(NexusActor actor, string action) => Invocations++;
}

/// <summary>
/// The governed execution order, in one place so the ordering is a property of the code rather than of
/// the tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: trusted authentication → NexusActor → owner authorization → governance → handler.</b>
/// </para>
/// <para>
/// The handler runs only on the last path. Every earlier refusal returns before it, which is what makes
/// "authorization failure does not reach governance" and "governance refusal cannot be bypassed by
/// authorization" structural rather than checked.
/// </para>
/// </remarks>
internal sealed class GovernedOperation(
    INexusAuthorizationPolicy policy,
    ITestGovernanceGate governance,
    ControlledHandler handler)
{
    public string Execute(NexusActor? actor, string action, string target)
    {
        // 1. Authentication. No actor means nothing downstream is asked anything.
        if (actor is null)
        {
            return "AuthenticationRequired";
        }

        // 2. Owner-side authorization.
        var decision = policy.Authorize(actor, action, target, scope: null);
        if (!decision.IsAuthorized)
        {
            return decision.Outcome.ToString();
        }

        // 3. Deterministic governance. An authorized actor may still be refused here.
        if (!governance.Decides(actor, action))
        {
            return "GovernanceRefused";
        }

        // 4. The controlled operation.
        handler.Handle(actor, action);
        return "Completed";
    }
}

/// <summary>
/// W10.8A TASK 6 and TASK 7 — the five authorization outcomes, and the execution ordering.
/// </summary>
public sealed class AuthorizationOrderingTests
{
    private static NexusActor Actor() => new()
    {
        ActorId = NexusActorId.Parse("nvx.actor.human.001"),
        ActorType = NexusActorType.Human,
        AuthenticationIssuer = LocalOsAuthenticationIssuer.IssuerName,
        AuthenticatedSubject = @"DURAI-DELL\Dell",
    };

    // ---------------------------------------------------------------- TASK 6 — the five outcomes

    [Fact]
    public void The_authorization_vocabulary_keeps_all_five_outcomes_distinct()
    {
        var outcomes = Enum.GetValues<AuthorizationOutcome>();

        Assert.Equal(5, outcomes.Length);
        Assert.Equal(5, outcomes.Distinct().Count());

        // Refusal is not failure: the caller may not proceed, and nothing went wrong.
        Assert.NotEqual(AuthorizationOutcome.Refused, AuthorizationOutcome.PermissionUnavailable);
        Assert.NotEqual(AuthorizationOutcome.Refused, AuthorizationOutcome.AuthenticationRequired);
        Assert.NotEqual(AuthorizationOutcome.PermissionUnavailable, AuthorizationOutcome.AuthenticationRequired);
        Assert.NotEqual(AuthorizationOutcome.InvalidScope, AuthorizationOutcome.Refused);
    }

    [Fact]
    public void Only_Authorized_is_authorized()
    {
        // Every other outcome refuses. A future member that defaulted to permitted would fail here.
        foreach (var outcome in Enum.GetValues<AuthorizationOutcome>())
        {
            var decision = new AuthorizationDecision
            {
                Outcome = outcome,
                Reason = "test",
                DecidingAuthority = "test",
            };

            Assert.Equal(outcome is AuthorizationOutcome.Authorized, decision.IsAuthorized);
        }
    }

    // ---------------------------------------------------------------- TASK 7 — the ordering

    [Fact]
    public void Unauthenticated_does_not_reach_governance_or_the_handler()
    {
        var handler = new ControlledHandler();
        var governance = new CountingGate(allow: true);
        var operation = new GovernedOperation(new AllowingPolicy(), governance, handler);

        var result = operation.Execute(actor: null, "verify", "release:1");

        Assert.Equal("AuthenticationRequired", result);
        Assert.Equal(0, governance.Evaluations);   // governance was never asked
        Assert.Equal(0, handler.Invocations);      // and nothing happened
    }

    [Fact]
    public void Unauthorized_does_not_reach_governance_or_the_handler()
    {
        var handler = new ControlledHandler();
        var governance = new CountingGate(allow: true);
        var operation = new GovernedOperation(new RefusingPolicy(), governance, handler);

        var result = operation.Execute(Actor(), "verify", "release:1");

        Assert.Equal(nameof(AuthorizationOutcome.Refused), result);
        Assert.Equal(0, governance.Evaluations);
        Assert.Equal(0, handler.Invocations);
    }

    [Fact]
    public void Authorized_but_refused_by_governance_does_not_reach_the_handler()
    {
        // An authorized actor may still be refused by governance, and authorization does not bypass it.
        var handler = new ControlledHandler();
        var governance = new CountingGate(allow: false);
        var operation = new GovernedOperation(new AllowingPolicy(), governance, handler);

        var result = operation.Execute(Actor(), "verify", "release:1");

        Assert.Equal("GovernanceRefused", result);
        Assert.Equal(1, governance.Evaluations);   // governance WAS asked
        Assert.Equal(0, handler.Invocations);      // and still nothing happened
    }

    [Fact]
    public void Authorized_and_permitted_by_governance_reaches_the_handler_exactly_once()
    {
        // The positive twin. Without it, every assertion above would pass on code that never reached the
        // handler at all — which is the failure mode a negative-only suite cannot see.
        var handler = new ControlledHandler();
        var governance = new CountingGate(allow: true);
        var operation = new GovernedOperation(new AllowingPolicy(), governance, handler);

        var result = operation.Execute(Actor(), "verify", "release:1");

        Assert.Equal("Completed", result);
        Assert.Equal(1, governance.Evaluations);
        Assert.Equal(1, handler.Invocations);
    }

    private sealed class CountingGate(bool allow) : ITestGovernanceGate
    {
        public int Evaluations { get; private set; }

        public bool Allow => allow;

        public bool Decides(NexusActor actor, string action)
        {
            Evaluations++;
            return allow;
        }
    }
}
