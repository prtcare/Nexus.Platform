using System.Security.Cryptography;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// Content identity of a released artifact: an algorithm plus a lowercase hex digest.
///
/// <para>
/// W9.1's promotion proof compares THIS value across environments. The comparison is the
/// whole mechanism: an environment holds the declared bytes only if its digest equals the
/// manifest's digest. A rebuild that "should" be identical is not accepted — only a digest
/// match is. W8F established that this estate can produce byte-identical builds
/// (<c>CA30386E…</c>, three builders, no <c>--no-dependencies</c>), which is what makes the
/// assertion meaningful rather than aspirational.
/// </para>
///
/// <para>
/// The digest is over the PACKAGED artifact, never over the source. Hashing the source
/// proves determinism; it does not prove identity, and identity is what promotion moves.
/// </para>
/// </summary>
public sealed record ArtifactDigest
{
    /// <summary>The only algorithm this contract accepts today. Recorded in the digest itself so a future second algorithm is a data change, not a format break.</summary>
    public const string Sha256Algorithm = "sha256";

    private ArtifactDigest(string algorithm, string hex)
    {
        Algorithm = algorithm;
        Hex = hex;
    }

    public string Algorithm { get; }

    /// <summary>Lowercase hexadecimal digest. Never empty, never mixed case.</summary>
    public string Hex { get; }

    /// <summary>
    /// Parses a digest written in the canonical <c>&lt;algorithm&gt;:&lt;hex&gt;</c> form.
    /// Refuses anything that is not exactly this shape rather than coercing it: a digest that
    /// is silently normalised is a digest that can silently compare equal to a different one.
    /// </summary>
    public static ArtifactDigest Parse(string value)
    {
        if (!TryParse(value, out var digest))
        {
            throw new ArgumentException(
                $"Not a canonical artifact digest (expected '{Sha256Algorithm}:<64 lowercase hex characters>').",
                nameof(value));
        }

        return digest!;
    }

    public static bool TryParse(string? value, out ArtifactDigest? digest)
    {
        digest = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var algorithm = value[..separator];
        var hex = value[(separator + 1)..];

        if (!string.Equals(algorithm, Sha256Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (hex.Length != 64 || !hex.All(IsLowerHex))
        {
            return false;
        }

        digest = new ArtifactDigest(algorithm, hex);
        return true;
    }

    /// <summary>Computes the digest of an artifact's bytes. The only place a digest is created from content.</summary>
    public static ArtifactDigest Compute(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hash = SHA256.HashData(content);
        return new ArtifactDigest(Sha256Algorithm, Convert.ToHexStringLower(hash));
    }

    public static ArtifactDigest Compute(ReadOnlySpan<byte> content)
    {
        var hash = SHA256.HashData(content);
        return new ArtifactDigest(Sha256Algorithm, Convert.ToHexStringLower(hash));
    }

    /// <summary>The canonical <c>&lt;algorithm&gt;:&lt;hex&gt;</c> wire form.</summary>
    public override string ToString() => $"{Algorithm}:{Hex}";

    private static bool IsLowerHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
}
