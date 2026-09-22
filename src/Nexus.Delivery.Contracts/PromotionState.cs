using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// The states a bundle occupies. Terminal states are per (bundle, environment) pair, not global: a
/// bundle that is <see cref="Live"/> in ENV-PROD is still <see cref="VerifiedTest"/> elsewhere.
///
/// <para>
/// <see cref="Quarantined"/> is deliberately part of the machine rather than an out-of-band flag.
/// A machine that can only say "failed" or "live" has no way to express <i>"do not promote this, for
/// a reason that is not a test failure."</i> W9.0 produced exactly such a reason: a commit carrying a
/// live-class credential, whose reachability is a fact about the source, not about the build.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PromotionState
{
    /// <summary>The build produced a bundle. Nothing is registered and nothing is deployed.</summary>
    Built,

    /// <summary>The bundle is immutable in the registry and addressable by digest.</summary>
    Registered,

    DeployedDev,

    VerifiedDev,

    DeployedTest,

    VerifiedTest,

    DeployedProd,

    /// <summary>Deployed to ENV-PROD and observed serving. The only state a real user sees.</summary>
    Live,

    /// <summary>Rolled back to the previous bundle in this environment.</summary>
    RolledBack,

    /// <summary>A gate failed, or the unit never became ready. Terminal for this bundle in this environment.</summary>
    Failed,

    /// <summary>
    /// Must not be promoted further, in any environment, until an Owner clears it. Reachable from any
    /// state by Owner action only; no transition clears it.
    /// </summary>
    Quarantined
}
