using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>The outcome of scanning a build's whole input set.</summary>
public sealed record BuildInputScanResult(SecretScanEvidence Evidence, IReadOnlyList<string> Detail);

/// <summary>
/// Scans the complete set of inputs a build consumes — not one repository, which is what W9.1's gate does.
///
/// <para>
/// <b>Why the set matters more than the scanner.</b> W9.1's gate scans the repository it runs in, which is
/// correct for a per-repository hook and insufficient for a release: a bundle is built from several
/// repositories, and a scan that covers one of them reports clean about one of them. The scanner itself is
/// unchanged; what is new is that it is pointed at every input, and that the result records which inputs
/// were covered so a caller can check the coverage rather than trust it.
/// </para>
///
/// <para>
/// <b>Quarantined subjects are scanned and reported, not skipped.</b> Skipping them would produce no
/// record of the historical exposure — which is precisely the mistake W8F made when it concluded that no
/// committed secret value existed. They are scanned, their findings are recorded separately, and they do
/// not block certification, because a build cannot fix superseded material and blocking on it would make
/// every build in the estate uncertifiable.
/// </para>
/// </summary>
public sealed class BuildInputScanner
{
    private readonly Func<SecretScanPolicy, SecretScanner> _scannerFactory;
    private readonly SecretScanPolicy _policy;

    public BuildInputScanner(SecretScanPolicy? policy = null)
    {
        _policy = policy ?? SecretScanPolicy.Neutral;
        _scannerFactory = p => new SecretScanner(p);
    }

    public BuildInputScanResult Scan(BuildInputSet inputSet)
    {
        ArgumentNullException.ThrowIfNull(inputSet);

        var scanner = _scannerFactory(_policy);
        var activeFindings = new List<string>();
        var quarantinedFindings = new List<string>();
        var incomplete = new List<string>();
        var detail = new List<string>();

        var filesScanned = 0;
        var filesSkipped = 0;
        var activeSubjectLabels = new List<string>();
        var quarantinedSubjectLabels = new List<string>();

        foreach (var subject in inputSet.Subjects)
        {
            var result = scanner.ScanDirectory(subject.RootPath);

            filesScanned += result.FilesScanned;
            filesSkipped += result.FilesSkipped;

            var locations = result.Findings
                .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
                .ThenBy(f => f.LineNumber)
                .Select(f => $"{subject.Label}/{f.RelativePath}:{f.LineNumber} (rule={f.RuleName}, key={f.KeyName})")
                .ToList();

            if (subject.BlocksCertification)
            {
                activeSubjectLabels.Add(subject.Label);
                activeFindings.AddRange(locations);

                foreach (var reason in result.IncompleteReasons)
                {
                    incomplete.Add($"{subject.Label}/{reason}");
                }

                detail.Add($"{subject.Label} [active] verdict={result.Verdict} scanned={result.FilesScanned} findings={locations.Count}");
            }
            else
            {
                quarantinedSubjectLabels.Add(subject.Label);
                quarantinedFindings.AddRange(locations);

                // A quarantined subject that could not be read is also not a reason to block: it is not a
                // build input. It is still recorded, in the detail, so the gap is visible.
                detail.Add($"{subject.Label} [quarantined-historical] verdict={result.Verdict} scanned={result.FilesScanned} findings={locations.Count}");

                if (!string.IsNullOrWhiteSpace(subject.QuarantineJustification))
                {
                    detail.Add($"    quarantined because: {subject.QuarantineJustification}");
                }
            }
        }

        // Findings outrank Incomplete, matching the single-root scanner: a known problem is more actionable
        // than an unknown one, and reporting Incomplete when a finding is already in hand would bury it.
        var verdict = activeFindings.Count > 0
            ? SecretScanVerdict.Findings
            : incomplete.Count > 0
                ? SecretScanVerdict.Incomplete
                : SecretScanVerdict.Clean;

        var evidence = new SecretScanEvidence(
            verdict,
            activeSubjectLabels,
            quarantinedSubjectLabels,
            filesScanned,
            filesSkipped,
            activeFindings,
            incomplete,
            quarantinedFindings);

        return new BuildInputScanResult(evidence, detail);
    }
}
