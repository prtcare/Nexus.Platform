using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// What kind of thing a deployment unit is. The kind decides which contract obligations apply:
/// a <see cref="Service"/> must declare a readiness signal and a deployment medium, and a
/// <see cref="Client"/> is the kind for which build-once is hardest (see the Owner's D1 ruling:
/// client configuration must move out of build-time compilation).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentUnitKind
{
    /// <summary>Can be deployed and reached over the network. Owes a readiness signal.</summary>
    Service,

    /// <summary>Reaches services; shipped to a browser or a desktop. Cannot hold a secret.</summary>
    Client,

    /// <summary>Consumed at build time rather than deployed. Promoted in lockstep with its consumers.</summary>
    Library
}
