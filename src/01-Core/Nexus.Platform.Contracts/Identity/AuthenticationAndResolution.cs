namespace Nexus.Platform.Contracts.Identity;

/// <summary>
/// A subject an issuer has authenticated, before Nexus has decided what it means.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the far side of the trust boundary, and it is not an identity.</b> An OS account, an OpenID
/// <c>sub</c> or a service name proves that <em>someone</em> was authenticated by <em>something</em>; it
/// says nothing about who they are in Nexus. The separation is the architecture: an issuer is replaceable,
/// and a product's account model is not Nexus's actor model.
/// </para>
/// <para>
/// <b>It cannot be constructed from a request body by accident.</b> The type carries no parse-from-string
/// helper and is produced only by an <see cref="IAuthenticationIssuer"/> — so the only way to hold one is
/// to have authenticated something.
/// </para>
/// </remarks>
public sealed record AuthenticatedSubject
{
    /// <summary>Which issuer established this subject. Matches <see cref="IAuthenticationIssuer.Issuer"/>.</summary>
    public required string Issuer { get; init; }

    /// <summary>The subject in the issuer's own vocabulary. Never used as a Nexus actor identifier.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// What the issuer authenticated — a person or a service.
    /// </summary>
    /// <remarks>
    /// The issuer states it because only the issuer knows: an OS account connecting over a named pipe
    /// could be either, and the transport cannot tell. The resolver validates that the statement is
    /// consistent with what it knows, rather than trusting it.
    /// </remarks>
    public required NexusActorType ActorType { get; init; }
}

/// <summary>
/// Establishes that a caller is who it claims, without believing anything the caller said.
/// </summary>
/// <remarks>
/// <para>
/// <b>An issuer is a boundary, not a lookup.</b> The distinction this milestone exists to draw: resolving
/// an identity from a value the caller supplied is a lookup, and a lookup answers "what record has this
/// name" rather than "is this caller who it names". An implementation of this interface must obtain its
/// answer from the execution boundary — the OS, a signed token, a mutual-TLS peer — and never from a field
/// in the request.
/// </para>
/// <para>
/// <b>Multiple issuers are expected, and are the point.</b> A local-OS issuer is the first; an OpenID or
/// Entra issuer is the intended successor. Because a resolution maps
/// <c>(issuer, subject) -&gt; actor</c>, adding an issuer adds identities rather than changing them, and no
/// owner-side command contract has to move.
/// </para>
/// <para>
/// <b>Absent evidence is not a permissive default.</b> <see cref="AuthenticateAsync"/> returns null when it
/// cannot establish a subject — no connection identity, no token, a token that fails validation. It does
/// not return a placeholder, and there is deliberately no anonymous subject anywhere in this contract.
/// </para>
/// </remarks>
public interface IAuthenticationIssuer
{
    /// <summary>
    /// This issuer's stable name, recorded on every actor it helps resolve.
    /// </summary>
    /// <remarks>
    /// Lower-case and hyphenated by convention (<c>local-os</c>). Never a credential, an endpoint or a
    /// file path — it is written into audit records and read by people reviewing them.
    /// </remarks>
    string Issuer { get; }

    /// <summary>
    /// Authenticates the current caller, or returns null when it cannot.
    /// </summary>
    /// <remarks>
    /// Null is a refusal and every caller must treat it as one. There is no "anonymous" return value,
    /// because a sentinel for it would eventually be treated as a subject.
    /// </remarks>
    ValueTask<AuthenticatedSubject?> AuthenticateAsync(CancellationToken cancellationToken = default);
}

/// <summary>What happened when a subject was offered to the resolver.</summary>
/// <remarks>
/// <b>Five outcomes, and they are not interchangeable.</b> The directive requires that an unknown actor
/// never silently becomes an anonymous authorized one, and that requires the refusal to be nameable.
/// Collapsing these into a nullable actor would lose the difference between "nobody was authenticated" and
/// "somebody was authenticated and Nexus has no actor for them" — the first is a missing credential, the
/// second is a provisioning gap, and an operator investigates them in different places.
/// </remarks>
public enum ActorResolutionOutcome
{
    /// <summary>An actor was resolved.</summary>
    Resolved = 0,

    /// <summary>
    /// No subject was authenticated at all.
    /// </summary>
    /// <remarks>The caller presented no evidence. Not the same as evidence Nexus did not recognise.</remarks>
    NotAuthenticated = 1,

    /// <summary>
    /// A subject was authenticated and Nexus has no actor for it.
    /// </summary>
    /// <remarks>
    /// <b>Fails closed.</b> This is the outcome that must never resolve to a default actor — an
    /// authenticated stranger is still a stranger, and the estate's habit of trusting a supplied
    /// <c>userId</c> is precisely the failure this replaces.
    /// </remarks>
    UnknownSubject = 2,

    /// <summary>An actor exists for this subject and is disabled or revoked.</summary>
    /// <remarks>
    /// Distinct from <see cref="UnknownSubject"/> because the remedy differs: a revocation is a decision
    /// somebody made and can be reviewed; a missing actor is a record nobody created.
    /// </remarks>
    Disabled = 3,

    /// <summary>The subject or the resolution itself is malformed.</summary>
    InvalidIdentity = 4,
}

/// <summary>The typed result of resolving an authenticated subject to a Nexus actor.</summary>
public sealed record ActorResolution
{
    /// <summary>What happened.</summary>
    public required ActorResolutionOutcome Outcome { get; init; }

    /// <summary>
    /// The actor, present if and only if <see cref="Outcome"/> is <see cref="ActorResolutionOutcome.Resolved"/>.
    /// </summary>
    public NexusActor? Actor { get; init; }

    /// <summary>Why, in the resolver's terms. Never blank for a refusal.</summary>
    public required string Detail { get; init; }

    /// <summary>A resolved actor, or a refusal that carries its reason.</summary>
    public static ActorResolution Resolved(NexusActor actor)
        => new() { Outcome = ActorResolutionOutcome.Resolved, Actor = actor, Detail = "Resolved." };

    /// <summary>A refusal. The actor is absent, and stays absent.</summary>
    public static ActorResolution Refused(ActorResolutionOutcome outcome, string detail)
        => new() { Outcome = outcome, Actor = null, Detail = detail };
}

/// <summary>
/// Turns an authenticated subject into the Nexus actor it corresponds to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mapping is Nexus's, and this port is where that ownership is enforced.</b> An implementation
/// must be deterministic: the same issuer and subject resolve to the same actor every time, with no
/// dependence on the clock, the request, or the order of previous calls. A resolution that could vary
/// would make an audit trail unreliable in exactly the direction that matters — the same subject appearing
/// as two actors, or two subjects as one.
/// </para>
/// <para>
/// <b>This is not authentication and must never be asked to be.</b> It receives an
/// <see cref="AuthenticatedSubject"/>, which can only have been produced by an
/// <see cref="IAuthenticationIssuer"/>. An implementation that accepted a bare string would be a lookup,
/// and the estate already has three of those.
/// </para>
/// </remarks>
public interface INexusActorResolver
{
    /// <summary>Resolves a subject, refusing rather than defaulting when it cannot.</summary>
    ValueTask<ActorResolution> ResolveAsync(
        AuthenticatedSubject subject,
        CancellationToken cancellationToken = default);
}
