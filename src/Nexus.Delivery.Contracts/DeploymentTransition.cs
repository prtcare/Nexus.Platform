using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// The transitions of the deployment state machine. This single enum is also the lineage record's
/// action vocabulary — deliberately one enum rather than two, so that a transition can never be
/// recorded under a name that does not exist as a transition.
///
/// <para>
/// The vocabulary is closed and each member is a distinct, gated act. Every predicate in
/// <c>Nexus.Delivery.Core.DeploymentStateMachine</c> is total over this enum, so adding a member
/// without a gate is a build-visible act rather than a silent default.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeploymentTransition
{
    /// <summary>Source must be clean. A dirty tree cannot name the source it came from.</summary>
    Build,

    /// <summary>The registry must accept the manifest, and must refuse a second write of the same bundle id.</summary>
    Register,

    DeployToDev,

    /// <summary>Requires an observed readiness signal, a matching digest in target, and a matching migration set.</summary>
    VerifyInDev,

    PromoteToTest,

    VerifyInTest,

    /// <summary>Owner-reserved.</summary>
    PromoteToProd,

    VerifyInProd,

    /// <summary>Any state to <see cref="PromotionState.Failed"/>. Permitted without authorization: a failure is an observation, not a decision.</summary>
    Fail,

    /// <summary>From <see cref="PromotionState.Live"/>. Requires the previous bundle to exist and to carry its own authorization.</summary>
    Rollback,

    /// <summary>Any state to <see cref="PromotionState.Quarantined"/>. Owner-reserved, and requires a reason.</summary>
    Quarantine,

    /// <summary>Owner-reserved. The ONLY transition out of <see cref="PromotionState.Quarantined"/>.</summary>
    ClearQuarantine
}
