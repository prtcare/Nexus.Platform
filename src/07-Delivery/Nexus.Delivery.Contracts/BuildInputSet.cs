using System.Text.Json.Serialization;

namespace Nexus.Delivery.Contracts;

/// <summary>
/// How a repository or directory relates to the build being certified.
///
/// <para>
/// <b>The distinction this type exists for.</b> W9.0 found a live-class credential in a tracked file in
/// this estate — in <c>Archived/</c>, superseded material that no build consumes. Task 6 requires that
/// known historical quarantined material be classified <b>separately</b> from active build inputs, and
/// the reason is that conflating them produces one of two bad outcomes: either the estate can never
/// certify anything because of a file nothing builds, or a real credential in active input is dismissed
/// as "just old stuff". Keeping them in separate categories means a historical exposure is recorded and
/// tracked while an active-input exposure blocks certification outright.
/// </para>
///
/// <para>
/// <b>The classification is an assertion, not a discovery.</b> Nothing here can tell whether a file is
/// reachable by a build; a human or a build definition says so, and the manifest records who said it. A
/// subject misclassified as quarantined is a false negative that no scanner can detect on its own —
/// which is why <see cref="QuarantineJustification"/> is required and why the debt register carries the
/// fact that only the one file was examined, not the directory as a class.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScanSubjectClass
{
    /// <summary>Consumed by the build. A credential here blocks certification.</summary>
    ActiveBuildInput,

    /// <summary>
    /// Superseded history that no build consumes. Recorded and tracked, does not block — but only when a
    /// justification is supplied.
    /// </summary>
    QuarantinedHistorical
}

/// <summary>One repository or directory entering the build-input scan.</summary>
public sealed record ScanSubject
{
    public ScanSubject(string label, string rootPath, ScanSubjectClass classification, string? quarantineJustification = null)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("A scan subject must be labelled.", nameof(label));
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A scan subject must name a root.", nameof(rootPath));
        }

        if (classification == ScanSubjectClass.QuarantinedHistorical && string.IsNullOrWhiteSpace(quarantineJustification))
        {
            throw new ArgumentException(
                "A quarantined-historical subject must state why it is unreachable from a build. Without that, the classification is an assertion nobody can review.",
                nameof(quarantineJustification));
        }

        Label = label;
        RootPath = rootPath;
        Classification = classification;
        QuarantineJustification = string.IsNullOrWhiteSpace(quarantineJustification) ? null : quarantineJustification;
    }

    public string Label { get; }

    public string RootPath { get; }

    public ScanSubjectClass Classification { get; }

    public string? QuarantineJustification { get; }

    public bool BlocksCertification => Classification == ScanSubjectClass.ActiveBuildInput;
}

/// <summary>
/// The complete set of inputs a build consumes — the set Task 6 requires to be scanned.
///
/// <para>
/// W9.1's gate scans one repository, because one repository is where it runs. A release bundle is built
/// from several, so the scan must cover the whole input set or it certifies a build whose other inputs
/// nobody looked at. This type makes that set explicit so it can be scanned as a unit and recorded in the
/// manifest as a unit.
/// </para>
/// </summary>
public sealed record BuildInputSet
{
    public BuildInputSet(IReadOnlyList<ScanSubject> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        if (subjects.Count == 0)
        {
            throw new ArgumentException(
                "A build input set cannot be empty. An empty set would scan nothing and report clean.",
                nameof(subjects));
        }

        var duplicate = subjects
            .GroupBy(s => s.Label, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException($"A scan subject label appears more than once: '{duplicate.Key}'.", nameof(subjects));
        }

        Subjects = [.. subjects.OrderBy(s => s.Label, StringComparer.Ordinal)];
    }

    public IReadOnlyList<ScanSubject> Subjects { get; }

    public IEnumerable<ScanSubject> ActiveInputs => Subjects.Where(s => s.BlocksCertification);

    public IEnumerable<ScanSubject> QuarantinedInputs => Subjects.Where(s => !s.BlocksCertification);
}

/// <summary>
/// The secret-scan result as recorded in a manifest.
///
/// <para>
/// <b>Carries counts and locations, never values.</b> A finding is identified by file path and line — the
/// same discipline the scanner itself follows, for the same reason: a manifest is an artifact, it gets
/// copied and passed around, and a partial credential inside one reads as a real credential to whoever
/// finds it next.
/// </para>
/// </summary>
public sealed record SecretScanEvidence
{
    public SecretScanEvidence(
        SecretScanVerdict verdict,
        IReadOnlyList<string> scannedSubjectLabels,
        IReadOnlyList<string> quarantinedSubjectLabels,
        int filesScanned,
        int filesSkipped,
        IReadOnlyList<string> findingLocations,
        IReadOnlyList<string> incompleteReasons,
        IReadOnlyList<string>? quarantinedFindingLocations = null)
    {
        ArgumentNullException.ThrowIfNull(scannedSubjectLabels);
        ArgumentNullException.ThrowIfNull(findingLocations);
        ArgumentNullException.ThrowIfNull(incompleteReasons);

        if (scannedSubjectLabels.Count == 0)
        {
            throw new ArgumentException(
                "A scan that covered no subject cannot report a verdict. An unscanned build is not a clean build.",
                nameof(scannedSubjectLabels));
        }

        if (verdict == SecretScanVerdict.Clean && findingLocations.Count > 0)
        {
            throw new ArgumentException("A clean verdict cannot carry findings in active input.", nameof(verdict));
        }

        if (verdict == SecretScanVerdict.Findings && findingLocations.Count == 0)
        {
            throw new ArgumentException("A findings verdict must name at least one active-input location.", nameof(verdict));
        }

        Verdict = verdict;
        ScannedSubjectLabels = [.. scannedSubjectLabels];
        QuarantinedSubjectLabels = [.. quarantinedSubjectLabels];
        FilesScanned = filesScanned;
        FilesSkipped = filesSkipped;
        FindingLocations = [.. findingLocations];
        IncompleteReasons = [.. incompleteReasons];
        QuarantinedFindingLocations = [.. quarantinedFindingLocations ?? []];
    }

    public SecretScanVerdict Verdict { get; }

    /// <summary>Subjects that were scanned — the active build inputs.</summary>
    public IReadOnlyList<string> ScannedSubjectLabels { get; }

    /// <summary>Subjects classified quarantined-historical, recorded so the exclusion is visible.</summary>
    public IReadOnlyList<string> QuarantinedSubjectLabels { get; }

    public int FilesScanned { get; }

    public int FilesSkipped { get; }

    /// <summary><c>path:line</c> entries from <b>active build inputs</b>. No part of any matched value.</summary>
    public IReadOnlyList<string> FindingLocations { get; }

    /// <summary>
    /// <c>path:line</c> entries from quarantined-historical subjects, recorded but <b>not</b> reflected in
    /// <see cref="Verdict"/>.
    ///
    /// <para>
    /// This is the separation Task 6 requires, and it is deliberately asymmetric. A credential in active
    /// build input blocks certification outright, because the artifact may contain it. A credential in
    /// superseded material that no build consumes is a real fact about the repository that must be
    /// <i>recorded</i> — deleting it from the record would be the "no exposure event found" mistake W8F
    /// made — while blocking nothing, because blocking on it would make every build in the estate
    /// uncertifiable for a reason no build can fix.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> QuarantinedFindingLocations { get; }

    public IReadOnlyList<string> IncompleteReasons { get; }

    /// <summary>A clean verdict over active input is the only passing result. Findings and Incomplete both block.</summary>
    public bool IsPassing => Verdict == SecretScanVerdict.Clean;
}
