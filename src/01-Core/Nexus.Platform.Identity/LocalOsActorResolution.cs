using Nexus.Platform.Contracts.Identity;

namespace Nexus.Platform.Identity;

/// <summary>
/// The authenticated peer of an execution boundary that carries OS identity, as the transport observed it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only place an external subject enters Nexus, and the transport writes it.</b> On a
/// Windows named pipe the peer account is established by the operating system during connection — the
/// client cannot choose it. W10.8A measured that: a client asserting <c>DOMAIN\someone-else</c> produced no
/// change in what the server observed.
/// </para>
/// <para>
/// <b>It is an interface so that the transport is replaceable and the failure is testable.</b> A named pipe
/// populates it; a loopback HTTP connection leaves it empty; a future OIDC boundary populates it from a
/// validated token. What no implementation may do is populate it from the request payload — an
/// implementation that read a header would satisfy this interface and destroy its purpose, which is why
/// the property is named for the *peer* rather than for a caller's claim.
/// </para>
/// </remarks>
public interface IAuthenticatedPeerContext
{
    /// <summary>
    /// The peer's OS account name, or null when the boundary established no peer identity.
    /// </summary>
    /// <remarks>
    /// Null is the honest answer for a transport that carries no peer identity — loopback HTTP, an
    /// in-process call, an unconnected pipe. It is a refusal, not a default.
    /// </remarks>
    string? PeerUser { get; }
}

/// <summary>
/// A peer context that establishes nothing.
/// </summary>
/// <remarks>
/// The default registration, and the reason is the estate's most expensive lesson: <b>a permissive default
/// becomes the architecture.</b> If the container resolves a peer context that names somebody, every
/// unconfigured deployment authenticates as that somebody. This one names nobody, so an estate that forgets
/// to wire a real one refuses every request instead of admitting every request.
/// </remarks>
public sealed class NoPeerContext : IAuthenticatedPeerContext
{
    /// <inheritdoc />
    public string? PeerUser => null;
}

/// <summary>
/// Authenticates a caller by the OS account the execution boundary observed.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the local-first issuer the Owner selected, and it is deliberately the smallest one.</b> It
/// proves a subject the caller cannot choose and nothing else: not a role, not a permission, not an
/// entitlement. Everything downstream that needs those asks a different component, which is what makes an
/// issuer replaceable — a cloud issuer will contradict none of this.
/// </para>
/// <para>
/// <b><see cref="Issuer"/> is recorded on every actor it resolves, and it is a name rather than a
/// mechanism.</b> <c>local-os</c> says which authority was trusted; it does not say how, and it must not —
/// an identifier that encoded a mechanism would have to change when the mechanism did, and every audit
/// record holding it would become a record of something that no longer exists.
/// </para>
/// </remarks>
public sealed class LocalOsAuthenticationIssuer : IAuthenticationIssuer
{
    /// <summary>The issuer's stable name. Recorded on every actor resolved through it.</summary>
    public const string IssuerName = "local-os";

    private readonly IAuthenticatedPeerContext _peer;

    /// <summary>Composes the issuer over the boundary's observed peer.</summary>
    public LocalOsAuthenticationIssuer(IAuthenticatedPeerContext peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        _peer = peer;
    }

    /// <inheritdoc />
    public string Issuer => IssuerName;

    /// <inheritdoc />
    public ValueTask<AuthenticatedSubject?> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var peer = _peer.PeerUser;

        // No peer identity is a refusal. There is no anonymous subject in this contract and returning a
        // placeholder here would put one in, through the front door.
        if (string.IsNullOrWhiteSpace(peer))
        {
            return ValueTask.FromResult<AuthenticatedSubject?>(null);
        }

        // Padded on either side is refused rather than trimmed. Two strings that differ only in padding are
        // the same account to a human and different actors to an ordinal comparison, and this estate
        // compares ordinally throughout.
        if (!string.Equals(peer, peer.Trim(), StringComparison.Ordinal))
        {
            return ValueTask.FromResult<AuthenticatedSubject?>(null);
        }

        return ValueTask.FromResult<AuthenticatedSubject?>(new AuthenticatedSubject
        {
            Issuer = IssuerName,
            Subject = peer,

            // An OS account can be a person or a service account, and the transport cannot tell which.
            // Human is the only value this issuer can justify: a service principal is a claim Nexus has to
            // make deliberately, and inferring it from a name like "svc-" would be the naming-as-authority
            // mistake this milestone exists to remove.
            ActorType = NexusActorType.Human,
        });
    }
}

/// <summary>One actor Nexus knows about, and which external subjects resolve to it.</summary>
/// <remarks>
/// <b>Identity records, not grants.</b> This type says <i>who</i> an actor is and which authenticated
/// subjects are them. It says nothing about what they may do — that question belongs to an
/// <c>INexusAuthorizationPolicy</c>, and keeping the two apart is what stops identity from silently
/// becoming permission.
/// </remarks>
public sealed record NexusActorRecord
{
    /// <summary>The Nexus-owned identifier.</summary>
    public required NexusActorId ActorId { get; init; }

    /// <summary>Whether this is a person or a service.</summary>
    public required NexusActorType ActorType { get; init; }

    /// <summary>
    /// The authenticated subjects that resolve to this actor, keyed by issuer name.
    /// </summary>
    /// <remarks>
    /// A set, because one actor may be reachable through several issuers — the local OS today, an OpenID
    /// subject tomorrow — and adding an issuer must add a way to reach the actor rather than create a
    /// second one.
    /// </remarks>
    public IReadOnlySet<string> Subjects { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether this actor is disabled.
    /// </summary>
    /// <remarks>
    /// <b>A disabled actor fails closed, and is reported distinctly from an unknown one.</b> The two send an
    /// operator to different places: a missing record is a provisioning gap, a disabled record is a
    /// decision somebody made and can be reviewed. Collapsing them loses the second.
    /// </remarks>
    public bool Disabled { get; init; }
}

/// <summary>
/// The authoritative set of actors Nexus knows, and which subjects are them.
/// </summary>
/// <remarks>
/// This is <b>not</b> a permission authority. It answers *who is this*; it cannot answer *what may they
/// do*, and an implementation must not be extended to try.
/// </remarks>
public interface INexusActorDirectory
{
    /// <summary>Finds the actor a subject belongs to, or null when no actor does.</summary>
    bool TryFindActor(string issuer, string subject, out NexusActorRecord? actor);
}

/// <summary>
/// Resolves an authenticated subject to a Nexus actor, deterministically and fail-closed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mapping is Nexus's, which is the whole point of the port.</b> An issuer authenticates an external
/// subject; this decides what that subject <em>is</em> in Nexus. The product that authenticated a subject
/// does not thereby define the actor — a chat user and a worker row may authenticate, and neither becomes a
/// Nexus identity by doing so.
/// </para>
/// <para>
/// <b>Deterministic by construction.</b> It reads a directory and nothing else: no clock, no request, no
/// mutable state. The same subject resolves to the same actor on every call, which is what makes an audit
/// trail a record of people rather than of sessions.
/// </para>
/// <para>
/// <b>There is no fallback, and that is the design.</b> An authenticated subject with no actor is refused.
/// A default — "if unknown, treat as the local operator" — is the single change that would make this whole
/// milestone decorative, and it is the change an implementation is most tempted to make.
/// </para>
/// </remarks>
public sealed class DeterministicNexusActorResolver : INexusActorResolver
{
    private readonly INexusActorDirectory _directory;

    /// <summary>Composes the resolver over the authoritative actor directory.</summary>
    public DeterministicNexusActorResolver(INexusActorDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public ValueTask<ActorResolution> ResolveAsync(
        AuthenticatedSubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (string.IsNullOrWhiteSpace(subject.Issuer) || string.IsNullOrWhiteSpace(subject.Subject))
        {
            return ValueTask.FromResult(ActorResolution.Refused(
                ActorResolutionOutcome.InvalidIdentity,
                "The authenticated subject is malformed: an issuer and a subject are both required."));
        }

        if (!_directory.TryFindActor(subject.Issuer, subject.Subject, out var record) || record is null)
        {
            return ValueTask.FromResult(ActorResolution.Refused(
                ActorResolutionOutcome.UnknownSubject,
                $"The issuer '{subject.Issuer}' authenticated '{subject.Subject}', and Nexus holds no actor "
                + "for that subject. This is a provisioning gap rather than a decision, and it refuses "
                + "rather than resolving to any actor."));
        }

        if (record.Disabled)
        {
            return ValueTask.FromResult(ActorResolution.Refused(
                ActorResolutionOutcome.Disabled,
                $"The actor '{record.ActorId}' is disabled. This is a recorded decision, not a missing "
                + "record, and it refuses."));
        }

        return ValueTask.FromResult(ActorResolution.Resolved(new NexusActor
        {
            ActorId = record.ActorId,
            ActorType = record.ActorType,
            AuthenticationIssuer = subject.Issuer,
            AuthenticatedSubject = subject.Subject,
        }));
    }
}
