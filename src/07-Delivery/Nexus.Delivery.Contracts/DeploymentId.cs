using System.Globalization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// <b>The identity of a deployment attempt</b>: what was deployed, where, and which attempt at it.
///
/// <para>
/// <b>Why this exists rather than reusing <see cref="DeploymentLineageRecord.LineageId"/>.</b> The two
/// answer different questions and the W9.4 re-establishment proved they cannot be conflated. A
/// <c>LineageId</c> is the identity of <b>an audit record</b> — it numbers the estate's append-only
/// <c>L-W9-*</c> series, and it is allocated by the ledger at the moment a record is written. A
/// <c>DeploymentId</c> is the identity of <b>a deployment</b> — it exists before any record is written,
/// it survives across the records that describe it, and two records about the same deployment carry the
/// same one.
/// </para>
///
/// <para>
/// <b>What went wrong without it.</b> Recovering ENV-DEV after the host restarted had no identity to be
/// recorded under. The recovery was a real, dated, materially distinct attempt, and the contract's only
/// deployment identity was the <c>L-W9-2</c> attached to the <i>original</i> deployment act. Retaining
/// it said nothing about the recovery; minting a new <c>L-W9-*</c> would have invented a transition that
/// never happened. Both answers were wrong because the question had no member to be asked in.
/// </para>
///
/// <para>
/// <b>Deterministic, not generated.</b> A <see cref="DeploymentId"/> is a pure function of the release,
/// the environment and the attempt ordinal, so two independently-running hosts computing it for the same
/// attempt produce the same string. Nothing here consults a clock, a random source or a counter file —
/// which is what makes it safe to state an intended <see cref="DeploymentId"/> in evidence written
/// <i>before</i> the deployment it names (see <c>DeploymentEvidenceTransaction</c>).
/// </para>
///
/// <para>
/// <b>Grammar.</b> <c>dep-&lt;release hex&gt;-&lt;dev|test|prod&gt;-&lt;attempt&gt;</c>, for example
/// <c>dep-2ec4c364727bcb74-dev-1</c>. The release hex is carried verbatim from
/// <see cref="ReleaseId.Value"/> so that the id can never name a release that does not exist.
/// </para>
/// </summary>
public sealed record DeploymentId
{
    public const string Prefix = "dep-";

    private DeploymentId(string value, ReleaseId release, DeploymentEnvironmentId environment, int attempt)
    {
        Value = value;
        Release = release;
        Environment = environment;
        Attempt = attempt;
    }

    /// <summary>The full identifier, e.g. <c>dep-2ec4c364727bcb74-dev-1</c>.</summary>
    public string Value { get; }

    /// <summary>The release this attempt deploys. Never inferred from the string.</summary>
    public ReleaseId Release { get; }

    /// <summary>The environment this attempt targets.</summary>
    public DeploymentEnvironmentId Environment { get; }

    /// <summary>The 1-based attempt ordinal for this release in this environment.</summary>
    public int Attempt { get; }

    /// <summary>
    /// The identifier for attempt number <paramref name="attempt"/> of <paramref name="release"/> in
    /// <paramref name="environment"/>. Attempts are 1-based: the first deployment of a release into an
    /// environment is attempt 1, and a materially distinct recovery or re-deployment is the next one.
    /// </summary>
    public static DeploymentId For(ReleaseId release, DeploymentEnvironmentId environment, int attempt)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(environment);

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempt),
                attempt,
                "Deployment attempts are numbered from 1. An attempt of 0 would be a deployment that never happened.");
        }

        var environmentToken = environment.Value switch
        {
            DeploymentEnvironmentId.Dev => "dev",
            DeploymentEnvironmentId.Test => "test",
            DeploymentEnvironmentId.Prod => "prod",
            _ => throw new ArgumentException(
                $"'{environment.Value}' is not a ratified environment, so no deployment id can be formed for it.",
                nameof(environment))
        };

        // The release's own sub-string is carried through untouched rather than re-derived, so the id can
        // only ever name a release id this estate can actually parse.
        var releaseHex = release.Value[ReleaseId.Prefix.Length..];

        return new DeploymentId(
            $"{Prefix}{releaseHex}-{environmentToken}-{attempt.ToString(CultureInfo.InvariantCulture)}",
            release,
            environment,
            attempt);
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value[Prefix.Length..].Split('-');

        return parts.Length == 3
            && parts[0].Length == ReleaseId.ShortFormHexLength
            && parts[0].All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'))
            && parts[1] is "dev" or "test" or "prod"
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var attempt)
            && attempt >= 1
            // No leading zeros: 'dev-01' and 'dev-1' would be two strings naming one attempt, which is the
            // aliasing a parsed identifier exists to prevent.
            && parts[2] == attempt.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses an identifier. <b>Recovers the release, environment and attempt from the string itself</b>,
    /// so a decoded record's members are the ones its id names and cannot disagree with them.
    /// </summary>
    public static DeploymentId Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"Not a deployment id (expected '{Prefix}' + {ReleaseId.ShortFormHexLength} lowercase hex characters "
                + $"+ '-' + dev|test|prod + '-' + attempt, e.g. '{Prefix}4c81be0a2fd97e35-dev-1'). Received '{value}'.",
                nameof(value));
        }

        var parts = value[Prefix.Length..].Split('-');

        return new DeploymentId(
            value,
            ReleaseId.Parse(ReleaseId.Prefix + parts[0]),
            parts[1] switch
            {
                "dev" => DeploymentEnvironmentId.DevEnv,
                "test" => DeploymentEnvironmentId.TestEnv,
                _ => DeploymentEnvironmentId.ProdEnv
            },
            int.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture));
    }

    public static bool TryParse(string? value, out DeploymentId? deployment)
    {
        if (!IsValid(value))
        {
            deployment = null;
            return false;
        }

        deployment = Parse(value!);
        return true;
    }

    public bool Equals(DeploymentId? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
