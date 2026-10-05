namespace Nexus.Delivery.Contracts;

/// <summary>
/// A unit's health surface. Readiness is separate from liveness because they answer different
/// questions: liveness is "the process is up", readiness is "the process can serve". Only readiness
/// can gate a promotion.
///
/// <para>
/// W9.0 measured that three of the estate's five services expose a single <c>/health</c> and cannot
/// distinguish the two. This contract makes the distinction an obligation for new declarations, so
/// the gap is closed by declaration rather than by hoping the endpoint means both.
/// </para>
/// </summary>
public sealed record HealthContract
{
    public HealthContract(string readinessPath, string? livenessPath = null)
    {
        if (string.IsNullOrWhiteSpace(readinessPath))
        {
            throw new ArgumentException(
                "A service unit must declare a readiness path. A promotion gate that cannot tell 'up' from 'ready' is not a gate.",
                nameof(readinessPath));
        }

        ReadinessPath = readinessPath;
        LivenessPath = string.IsNullOrWhiteSpace(livenessPath) ? null : livenessPath;
    }

    /// <summary>Required. The signal a promotion gate reads.</summary>
    public string ReadinessPath { get; }

    /// <summary>Optional. When absent, the readiness path is the only probe and the limitation is visible in the declaration.</summary>
    public string? LivenessPath { get; }
}
