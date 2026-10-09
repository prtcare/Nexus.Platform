namespace Nexus.Platform.Contracts.Identity;

/// <summary>
/// What an authorization evaluation concluded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Five outcomes, and every one is a refusal except the first.</b> The directive requires the result to
/// distinguish them, and the reason is that they tell an operator five different things: bring a
/// credential, ask for a grant, fix a malformed request, wait for a permission source to be configured, or
/// stop — you are not permitted.
/// </para>
/// <para>
/// <b><see cref="Refused"/> is not a failure.</b> The estate already draws this distinction elsewhere —
/// <c>RollbackPlan</c> states that *"REFUSAL is not a failure"* — and it holds here: a caller who is
/// authenticated, provisioned and simply not permitted has been handled correctly. Reporting it as an
/// error would make a well-governed refusal look like a defect in the platform.
/// </para>
/// </remarks>
public enum AuthorizationOutcome
{
    /// <summary>The actor may perform this action on this target.</summary>
    Authorized = 0,

    /// <summary>
    /// The actor is known and is not permitted.
    /// </summary>
    /// <remarks>A decision. Somebody could change it by granting the permission.</remarks>
    Refused = 1,

    /// <summary>
    /// No authenticated actor was present.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Refused"/> because the remedy is entirely different — the caller must
    /// authenticate, and no amount of permission-granting will help. Collapsing the two would produce a
    /// "not permitted" message for a request that never identified itself.
    /// </remarks>
    AuthenticationRequired = 2,

    /// <summary>
    /// The actor is authenticated and Nexus cannot determine what it may do.
    /// </summary>
    /// <remarks>
    /// <b>This is the outcome the estate is in today, and it is not the same as refusal.</b> No permission
    /// authority exists, so the honest answer to "may this actor do X" is frequently "Nexus does not know"
    /// rather than "no". Reporting it as <see cref="Refused"/> would state a decision nobody made.
    /// </remarks>
    PermissionUnavailable = 3,

    /// <summary>The target or scope is malformed, or names something outside the actor's reach.</summary>
    InvalidScope = 4,
}

/// <summary>One authorization decision, with the reason it was reached.</summary>
/// <remarks>
/// <b>The reason is required and must not be blank.</b> An authorization result an operator cannot
/// interrogate is one they will work around, and the estate's audit records already carry this lesson:
/// <c>IReleasePlanStore</c> records <c>BeforeHash</c>/<c>AfterHash</c> on refusals too, because *"a refused
/// write is as auditable as an accepted one."*
/// </remarks>
public sealed record AuthorizationDecision
{
    /// <summary>What was concluded.</summary>
    public required AuthorizationOutcome Outcome { get; init; }

    /// <summary>Why. Never blank.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Which authority decided.
    /// </summary>
    /// <remarks>
    /// Carried so a decision can be traced to its owner. It is not the same as the authentication issuer:
    /// an issuer says who established the actor, this says who said what the actor may do.
    /// </remarks>
    public required string DecidingAuthority { get; init; }

    /// <summary>Whether the actor may proceed. True only for <see cref="AuthorizationOutcome.Authorized"/>.</summary>
    public bool IsAuthorized => Outcome is AuthorizationOutcome.Authorized;
}

/// <summary>
/// Decides whether an authenticated actor may request an operation against a target.
/// </summary>
/// <remarks>
/// <para>
/// <b>Authorization is not authentication, and this port takes the second as given.</b> It receives a
/// <see cref="NexusActor"/> — which cannot be produced without an
/// <see cref="IAuthenticationIssuer"/> and an <see cref="INexusActorResolver"/> — so there is no overload
/// accepting a user id, and no implementation can be asked a question about an unauthenticated caller. That
/// is the structural form of *"Atlas must never make the authoritative decision itself"*: Atlas holds at
/// most a client to this port, and this port cannot be asked about anything Atlas asserts.
/// </para>
/// <para>
/// <b>Deterministic.</b> The same actor, action, target and scope must produce the same decision. No clock,
/// no request, no dependence on what was asked before. A permission model nobody can predict is one nobody
/// can review.
/// </para>
/// <para>
/// <b>Governance is a separate question and a separate port.</b> Authorization asks *may this actor request
/// this operation*; governance asks *is this operation allowed under current Nexus rules and state*. Both
/// may be required, neither implies the other, and passing one must never bypass the other. Keeping them on
/// two ports is what makes that checkable — one port answering both would make "authorized" and "allowed"
/// the same word.
/// </para>
/// </remarks>
public interface INexusAuthorizationPolicy
{
    /// <summary>The authority this policy decides on behalf of. Recorded on every decision.</summary>
    string Authority { get; }

    /// <summary>Decides whether the actor may request the action against the target.</summary>
    /// <param name="actor">An authenticated, resolved actor. Not an identifier and not a claim.</param>
    /// <param name="action">The operation, from the owning authority's own closed vocabulary.</param>
    /// <param name="target">What it would act upon, in the owning authority's terms.</param>
    /// <param name="scope">The declaring scope the operation would run within, where one applies.</param>
    /// <remarks>
    /// The parameters are strings by necessity — the vocabulary belongs to the owning authority, and this
    /// is a shared neutral contract that must not know Delivery's or DevelopmentControl's types. What is
    /// <em>not</em> a string is the actor.
    /// </remarks>
    AuthorizationDecision Authorize(
        NexusActor actor,
        string action,
        string target,
        string? scope);
}
