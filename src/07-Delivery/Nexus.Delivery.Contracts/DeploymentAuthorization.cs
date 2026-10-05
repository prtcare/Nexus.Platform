using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Who is acting on a deployment, in the terms the governance model defines.
///
/// <para>
/// Segregation of duty is the point: the party that promotes must not be the party that declares the
/// promotion successful, and the party that builds must not be able to alter what it registered.
/// The verification gates are mechanical — digest comparison, readiness, migration-set match — which
/// is what makes the separation achievable without doubling the human effort.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentAuthorityRole
{
    /// <summary>The pipeline. Builds and registers. May not promote.</summary>
    Builder,

    /// <summary>The delivery team. Deploys and verifies in ENV-DEV and ENV-TEST.</summary>
    DeliveryTeam,

    /// <summary>The Owner. The only role that may promote to ENV-PROD, clear a quarantine, or authorize a destructive migration.</summary>
    Owner,

    /// <summary>On-call. May trigger a rollback and request a quarantine. May not promote or clear.</summary>
    OnCall
}

/// <summary>
/// An authorization for one transition by one actor.
///
/// <para>
/// <see cref="IsOwnerAuthorized"/> is a separate member rather than a comparison against
/// <see cref="Role"/> at each call site, because the reservation must be enforced in one place. The
/// W8D precedent is exact: H-1 was reserved to the Owner <i>by the reader's own contract</i>, and the
/// honest exit was <c>HUMAN_DECISION_REQUIRED</c> rather than a workaround. A reservation that can
/// be bypassed by the party it reserves against is not a reservation.
/// </para>
/// </summary>
public sealed record DeploymentAuthorization
{
    public DeploymentAuthorization(
        DeploymentAuthorityRole role,
        string actorIdentity,
        DateTimeOffset authorizedAt)
    {
        if (string.IsNullOrWhiteSpace(actorIdentity))
        {
            throw new ArgumentException("An authorization must name the actor that gave it.", nameof(actorIdentity));
        }

        if (CredentialShape.LooksLikeCredentialValue(actorIdentity))
        {
            throw new ArgumentException(
                "An actor identity is name-shaped; a credential-shaped value is refused here because authorizations are recorded in lineage.",
                nameof(actorIdentity));
        }

        Role = role;
        ActorIdentity = actorIdentity;
        AuthorizedAt = authorizedAt;
    }

    public DeploymentAuthorityRole Role { get; }

    public string ActorIdentity { get; }

    public DateTimeOffset AuthorizedAt { get; }

    /// <summary>True only for an Owner authorization.</summary>
    public bool IsOwnerAuthorized => Role == DeploymentAuthorityRole.Owner;

    public static DeploymentAuthorization For(DeploymentAuthorityRole role, string actor, DateTimeOffset at)
        => new(role, actor, at);
}
