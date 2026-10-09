namespace Nexus.Platform.Contracts.Identity;

/// <summary>
/// What kind of thing is acting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two members, and the second one is why the first one is not simply called "user".</b> A background
/// worker, a scheduled job or a deployment process acts on Nexus, and it is not a person. Modelling it as
/// a fake human account makes every audit trail ambiguous about whether a human decided something, and
/// makes "who approved this" unanswerable in exactly the records where it matters most.
/// </para>
/// <para>
/// <b>Closed, and deliberately small.</b> A third member would be a claim that some other kind of actor
/// exists and has been thought about; adding one is a decision with a rationale, not a default.
/// </para>
/// </remarks>
public enum NexusActorType
{
    /// <summary>A person.</summary>
    Human = 0,

    /// <summary>
    /// A non-human principal: a service, a worker, a scheduled job.
    /// </summary>
    /// <remarks>
    /// Its <see cref="NexusActor.AuthenticatedSubject"/> identifies the service, and the authentication
    /// method that established it will differ from a human's. <b>A service token proves a service
    /// identity, never a human one</b> — an actor of this type is not evidence that a person was involved,
    /// and a record that needs a human decision must not accept one.
    /// </remarks>
    Service = 1,
}

/// <summary>
/// A Nexus actor identifier. Belongs to Nexus, and to nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The central decision of W10.8A, as a type.</b> Before this milestone the estate had no idea of an
/// actor that Nexus owned. It had <c>WindowsIdentity</c> nowhere, a chat user in one product, a
/// <c>Worker</c> row in another, a Mind caller, and — everywhere in the platform — a <c>userId</c> string
/// that arrived in a request body and was believed.
/// </para>
/// <para>
/// <b>The authentication mechanism does not define the actor identity.</b> An issuer authenticates an
/// <em>external subject</em> — an OS account, an OpenID subject, a service — and Nexus resolves that pair
/// to an actor it owns. The resolution is the boundary. An issuer can be replaced, added to, or moved to
/// the cloud without any actor changing identity, which is the property
/// <see cref="NexusActor.AuthenticationIssuer"/> exists to make possible.
/// </para>
/// <para>
/// <b>Validated at construction, because an invalid identifier is worse than a missing one.</b> A blank or
/// whitespace actor id would flow into an audit record, a lock owner and a permission decision, and every
/// one of those would silently be about nobody.
/// </para>
/// </remarks>
public readonly record struct NexusActorId
{
    private NexusActorId(string value) => Value = value;

    /// <summary>The identifier, as a string. Never blank.</summary>
    public string Value { get; }

    /// <summary>
    /// Parses an actor identifier, refusing anything that could not name an actor.
    /// </summary>
    /// <remarks>
    /// <b>Refuses rather than trims.</b> Trimming would accept <c>" alice"</c> and <c>"alice "</c> as the
    /// same actor while an issuer that emitted either would be, to every other component, a different
    /// actor. A boundary that silently normalises is a boundary whose two sides can disagree about who
    /// someone is.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is null, blank or padded.</exception>
    public static NexusActorId Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A Nexus actor identifier is required. Absence of an identity is not an identity, and "
                + "resolving it to one would make every downstream decision about nobody in particular.",
                nameof(value));
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{value}' is padded. An actor identifier is compared ordinally throughout, so a padded "
                + "value is a different actor from the same value unpadded.",
                nameof(value));
        }

        if (value.Length > 200)
        {
            throw new ArgumentException(
                $"An actor identifier may not exceed 200 characters; this one is {value.Length}.",
                nameof(value));
        }

        return new NexusActorId(value);
    }

    /// <summary>Whether this value names an actor. A default-constructed value does not.</summary>
    public bool IsSpecified => !string.IsNullOrEmpty(Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Who is acting, once the question has been answered by Nexus rather than asserted by the caller.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the whole point of W10.8A: it exists in no request body.</b> Every command, every
/// authorization decision and every audit record is about an instance of this type, and the only way to
/// obtain one is to resolve it from an <em>authenticated</em> subject through
/// <see cref="INexusActorResolver"/>. There is deliberately no factory on this type that takes a string
/// and returns an actor — a convenience like that would become the request-body identity within one
/// milestone.
/// </para>
/// <para>
/// <b>No roles and no permissions, deliberately.</b> Identity answers <i>who</i>; authorization answers
/// <i>may they</i>. Putting a permission set on the actor would make every decision a lookup on a value
/// carried with the identity, and the two questions would stop being separable — which is exactly the
/// state the estate is in today, where <c>Roles</c> and <c>Permissions</c> ride along with a caller-supplied
/// user id and nothing distinguishes a claim from a fact.
/// </para>
/// <para>
/// <b>The two halves of a resolution are both carried, and they are not redundant.</b>
/// <see cref="AuthenticationIssuer"/> and <see cref="AuthenticatedSubject"/> are what was proved;
/// <see cref="ActorId"/> is what Nexus decided it means. An audit record holding only the first cannot say
/// who did something in Nexus's terms; one holding only the second cannot say how it was established, and
/// so cannot be reviewed when an issuer is found to be untrustworthy.
/// </para>
/// </remarks>
public sealed record NexusActor
{
    /// <summary>
    /// The Nexus-owned identity. Stable across issuers, sessions and authentication methods.
    /// </summary>
    public required NexusActorId ActorId { get; init; }

    /// <summary>Whether this is a person or a service.</summary>
    public required NexusActorType ActorType { get; init; }

    /// <summary>
    /// Which issuer established <see cref="AuthenticatedSubject"/>, by name.
    /// </summary>
    /// <remarks>
    /// A stable, non-secret identifier for the authority — for example <c>local-os</c> — and never a path
    /// to a configuration file, an endpoint, or anything an operator could paste a credential into.
    /// </remarks>
    public required string AuthenticationIssuer { get; init; }

    /// <summary>
    /// The subject the issuer authenticated, in the issuer's own vocabulary.
    /// </summary>
    /// <remarks>
    /// An OS account name, an OpenID <c>sub</c>, a service name. <b>This is not an actor identifier and
    /// must never be used as one.</b> It is the evidence a resolution was made from, and it is recorded so
    /// that resolution can be re-examined or replayed if an issuer's trust is ever revisited.
    /// </remarks>
    public required string AuthenticatedSubject { get; init; }

    /// <summary>
    /// The tenant this actor resolved within, where the resolution is tenant-scoped.
    /// </summary>
    /// <remarks>
    /// Null means the resolution was not tenant-scoped, which is a different statement from resolving to an
    /// empty tenant. Nullable rather than defaulted so the two cannot be confused.
    /// </remarks>
    public string? TenantId { get; init; }
}
