using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>The verdict of a secret scan.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SecretScanVerdict
{
    /// <summary>Every scannable file was read and nothing matched.</summary>
    Clean,

    /// <summary>At least one finding. The build gate fails.</summary>
    Findings,

    /// <summary>
    /// The scan could not complete: a file was unreadable, or a budget was exceeded. <b>Never
    /// reported as clean.</b> A scan that cannot run is <c>NOT_RUN</c>, exactly as this estate
    /// reports a test suite it could not execute — the failure it guards against is a green that
    /// means nothing.
    /// </summary>
    Incomplete
}

/// <summary>
/// One match.
///
/// <para>
/// <b>Contains no part of the matched value.</b> Not a prefix, not a suffix, not a hash. A scanner's
/// output lands in build logs and CI artefacts, which are the wrong places for a partial credential,
/// and a partial credential in a log is indistinguishable from a full one to whoever reads it next.
/// The location, the key name, the rule and the length are what a person needs to fix the finding.
/// </para>
/// </summary>
public sealed record SecretScanFinding(
    string RelativePath,
    int LineNumber,
    string RuleName,
    string KeyName,
    int ValueLength);

/// <summary>The result of a scan over a root.</summary>
public sealed record SecretScanResult(
    SecretScanVerdict Verdict,
    IReadOnlyList<SecretScanFinding> Findings,
    int FilesScanned,
    int FilesSkipped,
    IReadOnlyList<string> IncompleteReasons)
{
    public bool BlocksBuild => Verdict != SecretScanVerdict.Clean;

    /// <summary>A finding summary safe to print: path, line, rule, key, length. Never a value.</summary>
    public IEnumerable<string> Describe()
        => Findings
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ThenBy(f => f.LineNumber)
            .Select(f => $"{f.RelativePath}:{f.LineNumber}  rule={f.RuleName}  key={f.KeyName}  valueLen={f.ValueLength} (value withheld)");
}
