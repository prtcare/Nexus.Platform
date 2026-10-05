namespace Nexus.Delivery.Contracts;

/// <summary>
/// Identity of a release bundle, and the rule that makes it meaningful: <b>changed bytes always mean
/// a new bundle, never a promotion.</b> That is the Owner's ruling, and it is the reason a bundle id
/// is immutable and never reused.
///
/// <para>
/// Convention: <c>nexus-&lt;yyyy.MM.dd&gt;-&lt;short-sha-or-run-token&gt;</c> — human-readable, sortable
/// by date, and never derived from a mutable counter. Suffix <c>-r&lt;n&gt;</c> when more than one
/// bundle is cut on the same day from the same commit, so that identity is never ambiguous.
/// </para>
/// </summary>
public sealed record BundleId
{
    private BundleId(string value) => Value = value;

    public string Value { get; }

    public static BundleId Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "A bundle id must be name-shaped (letters, digits, '-', '.', '_'; no unbroken run of 20+), "
                + "at least 3 characters, and must not begin with a digit or a separator. "
                + "Example: 'nexus-2026.09.22-7f3a1c'.",
                nameof(value));
        }

        return new BundleId(value);
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || value.Length > 120)
        {
            return false;
        }

        if (!char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.' or '_'))
            {
                return false;
            }
        }

        // Reuses the name-shape guard so a bundle id cannot itself be a credential-shaped string —
        // ids appear in paths, lineage records and logs, which is the wrong place for an
        // unbroken 20+ character run even in principle.
        return CredentialShape.LongestUnbrokenAlphanumericRun(value) < CredentialShape.CredentialRunThreshold;
    }

    public override string ToString() => Value;
}
