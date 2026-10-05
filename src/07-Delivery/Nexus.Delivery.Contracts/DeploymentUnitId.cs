namespace Nexus.Delivery.Contracts;

/// <summary>
/// Stable identity of a deployment unit — the smallest thing that can be released on its own.
///
/// <para>
/// Dotted, lowercase, and STABLE ACROSS RELEASES. Changing a unit's id is not a rename; it is
/// the retirement of one unit and the introduction of another, because history, lineage and
/// quarantine all key on this value.
/// </para>
/// </summary>
public sealed record DeploymentUnitId
{
    private DeploymentUnitId(string value) => Value = value;

    public string Value { get; }

    public static DeploymentUnitId Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "A deployment unit id must be dotted lowercase segments "
                + "(letters, digits, hyphen; at least one dot; no leading, trailing or doubled dot). "
                + "Example: 'nexus.developer.api'.",
                nameof(value));
        }

        return new DeploymentUnitId(value);
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split('.');
        if (segments.Length < 2)
        {
            return false;
        }

        return segments.All(segment =>
            segment.Length > 0
            && segment[0] != '-'
            && segment[^1] != '-'
            && segment.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'));
    }

    public override string ToString() => Value;
}
