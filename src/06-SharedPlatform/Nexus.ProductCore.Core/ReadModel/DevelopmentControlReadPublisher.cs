using System.Text;
using System.Text.Json;
using Nexus.ProductCore.Contracts.ReadModel;

namespace Nexus.ProductCore.Core.ReadModel;

/// <summary>What a publication attempt did, in one value a caller can act on.</summary>
public sealed record DevelopmentControlPublishOutcome(
    bool Published,
    string? Path,
    string PayloadDigest,
    string Reason)
{
    public static DevelopmentControlPublishOutcome Refused(string reason, string digest = "") =>
        new(false, null, digest, reason);
}

/// <summary>
/// W10.4 TASKs 6 and 7 — <b>publishes the DevelopmentControl read model atomically, to an explicit
/// destination.</b>
///
/// <para>
/// The rules are the ones W10.1 proved for Delivery, reused rather than re-derived: an explicit
/// destination, staging beside it and moving over it, refusals that replace nothing, and a semantic
/// digest over the payload alone so a metadata refresh is distinguishable from a state change.
/// </para>
///
/// <para>
/// <b>Read-only against the authority.</b> It opens the workbook for reading through the canonical
/// reader. It takes no lock, acquires no reservation, and never writes to the workbook or beside it.
/// A publisher that needed the writer lock to describe the authority would be claiming the right to
/// change it in order to report on it.
/// </para>
///
/// <para>
/// <b>The destination is a parameter, never a constant.</b> The consumer's location is a decision; a
/// caller that names nothing is refused rather than defaulted.
/// </para>
/// </summary>
public sealed class DevelopmentControlReadPublisher
{
    private readonly DevelopmentControlReadProjection _projection;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public DevelopmentControlReadPublisher(DevelopmentControlReadProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _projection = projection;
    }

    /// <summary>The digest a previous publication produced, or null when nothing is published.</summary>
    public static string? PublishedDigest(string destinationDirectory)
    {
        var path = Path.Combine(destinationDirectory, DevelopmentControlReadContract.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path).TrimStart('﻿'));
            return document.RootElement.TryGetProperty("Source", out var source)
                && source.TryGetProperty("PayloadDigest", out var digest)
                ? digest.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Projects, validates and publishes. <paramref name="observedAt"/> is supplied so a run
    /// is reproducible for a fixed input and a fixed instant.</summary>
    public DevelopmentControlPublishOutcome Publish(string destinationDirectory, string observedAt, string? sourceRevision = null)
    {
        var destinationCheck = ValidateDestination(destinationDirectory);
        if (destinationCheck is not null)
        {
            return DevelopmentControlPublishOutcome.Refused(destinationCheck);
        }

        DevelopmentControlReadPayload payload;
        try
        {
            payload = _projection.Project();
        }
        catch (DevelopmentControlAuthorityUnavailableException ex)
        {
            return DevelopmentControlPublishOutcome.Refused($"AUTHORITY_UNAVAILABLE — {ex.Message}");
        }
        catch (DevelopmentControlGovernanceRegistryInvalidException ex)
        {
            // W10.5B. The authority was reached; what it returned cannot be published. A refusal
            // rather than a repaired document, and deliberately NOT a silent skip of the offending
            // row: every repair available here — dropping the record, keeping the first duplicate,
            // synthesising an id from the row number — produces a valid-looking document whose gate
            // count is wrong, and the count is the thing a consumer acts on.
            return DevelopmentControlPublishOutcome.Refused($"GOVERNANCE_REGISTRY_INVALID — {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return DevelopmentControlPublishOutcome.Refused($"the DevelopmentControl authority could not be read: {ex.Message}");
        }

        var digest = DevelopmentControlReadProjection.SemanticDigest(payload);

        var model = new DevelopmentControlReadModel(
            DevelopmentControlReadContract.SchemaVersion,
            new DevelopmentControlReadSource(
                DevelopmentControlReadContract.SchemaVersion,
                DevelopmentControlReadContract.Authority,
                sourceRevision ?? "(unspecified)",
                sourceRevision ?? "(unspecified)",
                observedAt,
                observedAt,
                digest),
            payload);

        var refusal = Validate(model);
        if (refusal is not null)
        {
            return DevelopmentControlPublishOutcome.Refused(refusal, digest);
        }

        // Serialised ONCE. Every retry below commits these exact bytes.
        var json = JsonSerializer.Serialize(model, WriteOptions);
        var finalPath = Path.Combine(destinationDirectory, DevelopmentControlReadContract.FileName);

        return CommitAtomically(finalPath, json, digest, out var commitReason)
            ? new DevelopmentControlPublishOutcome(true, finalPath, digest, "published")
            : DevelopmentControlPublishOutcome.Refused(commitReason, digest);
    }

    // ================================================================ the commit boundary

    // W10.4 REMEDIATION. This boundary is deliberately DUPLICATED from DeliveryReadPublisher rather
    // than shared, and the reason is structural, not an oversight: the two publishers live in
    // different assemblies (Nexus.ProductCore.Core and Nexus.Delivery.Core), and ProductCore.Core has
    // zero references BY CONSTRUCTION — see its csproj. A shared helper would require a new common
    // assembly, which is an architectural change this remediation is not authorised to make.
    //
    // The duplication is the lesser evil here, and the defect being fixed is the same one: the staging
    // path was the fixed string `finalPath + ".staging"`, so two concurrent publishers to one
    // destination shared a single scratch file and one was denied. Left unfixed in this sibling, the
    // identical defect would simply be waiting for its own parallel test to be written.

    // The attempt ceiling is a backstop, not the real bound: CommitBudget is. The first version used 5
    // attempts with a flat small backoff, which exhausted all five in ~216 ms — consuming 11% of the
    // budget and giving up while most of the contention window remained. MEASURED, from the retry
    // message the publisher itself emits.
    private const int MaxCommitAttempts = 20;

    private static readonly TimeSpan[] CommitBackoff =
    [
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(160),
        TimeSpan.FromMilliseconds(200)
    ];

    private static readonly TimeSpan CommitBudget = TimeSpan.FromSeconds(2);

    /// <summary>Per-destination gate. Narrower than a process-wide lock, and sufficient.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> CommitGates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The stage-then-move commit, made robust to transient contention: a unique scratch path per
    /// attempt-run, and a bounded retry on contention only. Nothing upstream is repeated —
    /// <paramref name="json"/> is the already-validated payload, computed once by the caller.
    /// </summary>
    private bool CommitAtomically(string finalPath, string json, string digest, out string reason)
    {
        var stagingPath = finalPath + "." + Guid.NewGuid().ToString("N")[..12] + ".staging";

        var gate = CommitGates.GetOrAdd(finalPath, _ => new object());
        var clock = System.Diagnostics.Stopwatch.StartNew();

        lock (gate)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllText(stagingPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    File.Move(stagingPath, finalPath, overwrite: true);

                    reason = "published";
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    TryRemoveStaging(stagingPath);

                    if (IsReadOnlyDestination(finalPath))
                    {
                        reason = $"publication refused: '{finalPath}' is read-only. This is a configuration "
                               + "condition, not transient contention, so it is not retried.";
                        return false;
                    }

                    // W10.4 REMEDIATION, second pass. A destination that is a DIRECTORY can never be moved
                    // over, on any filesystem, so it is permanent — and Windows raises the SAME
                    // ERROR_ACCESS_DENIED for it as for a transient open handle. Retrying on the exception
                    // type alone therefore retried a condition that could not change: MEASURED at 20 attempts
                    // over 2054 ms before this guard existed. The exception type is a hint; the filesystem
                    // state is the evidence.
                    if (Directory.Exists(finalPath))
                    {
                        reason = $"publication refused: '{finalPath}' is a directory. A file cannot be moved over "
                               + "a directory on any filesystem, so this is a permanent condition and is not retried.";
                        return false;
                    }

                    var transient = IsTransientCommitContention(ex);
                    if (!transient || attempt >= MaxCommitAttempts || clock.Elapsed >= CommitBudget)
                    {
                        reason = transient
                            ? $"publication failed while staging '{stagingPath}': {ex.Message}. Retried "
                              + $"{attempt} time(s) over {clock.ElapsedMilliseconds} ms and gave up. The "
                              + "previously published snapshot, if any, is unchanged."
                            : $"publication failed while staging '{stagingPath}': {ex.Message}. This is not a "
                              + "transient-contention condition, so it is not retried. The previously "
                              + "published snapshot, if any, is unchanged.";
                        return false;
                    }

                    Thread.Sleep(CommitBackoff[Math.Min(attempt - 1, CommitBackoff.Length - 1)]);
                }
            }
        }
    }

    private static bool IsReadOnlyDestination(string finalPath)
    {
        try
        {
            return File.Exists(finalPath) && File.GetAttributes(finalPath).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The exceptions this boundary retries, enumerated. Anything else propagates unretried.</summary>
    private static bool IsTransientCommitContention(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
                // The measured defect: Windows raises this when another handle is open on the target.
                // ValidateDestination already proved the directory creatable, so a directory-level ACL
                // failure cannot reach this point.
                return true;

            case IOException io:
                const int ErrorSharingViolation = 32;
                const int ErrorLockViolation = 33;
                var code = io.HResult & 0xFFFF;
                return code is ErrorSharingViolation or ErrorLockViolation;

            default:
                return false;
        }
    }

    private static string? ValidateDestination(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return "no destination was named. The published location is a governed decision and is never "
                 + "guessed: pass one explicitly.";
        }

        if (!Path.IsPathRooted(destinationDirectory))
        {
            return $"'{destinationDirectory}' is not an absolute path. A relative destination resolves against "
                 + "the process's working directory, so two runs could publish to two places and neither would say so.";
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the destination '{destinationDirectory}' could not be used: {ex.Message}";
        }

        return null;
    }

    /// <summary>The contract's own preconditions, checked before anything is written.</summary>
    private static string? Validate(DevelopmentControlReadModel model)
    {
        if (!string.Equals(model.SchemaVersion, DevelopmentControlReadContract.SchemaVersion, StringComparison.Ordinal))
        {
            return $"the projection declared contract version '{model.SchemaVersion}', which is not "
                 + $"'{DevelopmentControlReadContract.SchemaVersion}'.";
        }

        if (string.IsNullOrWhiteSpace(model.Source.SourceId))
        {
            return "the projection does not identify its source.";
        }

        if (model.Source.PayloadDigest.Length != 71 || !model.Source.PayloadDigest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return $"the payload digest '{model.Source.PayloadDigest}' is not a sha256: prefixed lowercase hex digest.";
        }

        if (!DateTimeOffset.TryParse(model.Source.ObservedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _))
        {
            return $"the observation instant '{model.Source.ObservedAt}' is not a parseable timestamp.";
        }

        // W10.5B. The registry's availability and its rows are checked AGAINST EACH OTHER, because
        // the pairing is the entire safety property and a pairing that is only asserted upstream is
        // one refactor away from being asserted nowhere.
        //
        // The two documents this refuses are the two that would look correct to a reader:
        //   UNAVAILABLE carrying gates      — the source could not be read, yet rows are published.
        //                                      Whatever those rows are, they are not this reading.
        //   AVAILABLE, and nothing else    — the shape of a legitimate zero AND the shape of an
        //                                      unread source. Only the State distinguishes them, so
        //                                      the State must be one of the two known values; an
        //                                      unrecognised state is refused rather than rendered.
        var registry = model.Payload.Governance;
        if (registry is null)
        {
            return "the payload carries no Governance registry. It is a REQUIRED section: a document "
                 + "silent about governance is indistinguishable from one whose governance source was "
                 + "never consulted.";
        }

        if (registry.State is not (DevelopmentControlReadGovernanceRegistry.Available
                                or DevelopmentControlReadGovernanceRegistry.Unavailable))
        {
            return $"the Governance registry declares state '{registry.State}', which is neither "
                 + $"'{DevelopmentControlReadGovernanceRegistry.Available}' nor "
                 + $"'{DevelopmentControlReadGovernanceRegistry.Unavailable}'. An unrecognised state "
                 + "cannot be rendered as either an answer or an absence.";
        }

        if (registry.State == DevelopmentControlReadGovernanceRegistry.Unavailable && registry.Gates.Count != 0)
        {
            return $"the Governance registry declares '{DevelopmentControlReadGovernanceRegistry.Unavailable}' "
                 + $"and carries {registry.Gates.Count} gate(s). An unread source asserts nothing about gates, "
                 + "so publishing rows beside it would present a reading that was never taken.";
        }

        var blankId = registry.Gates.FirstOrDefault(g => string.IsNullOrWhiteSpace(g.GovernanceId));
        if (blankId is not null)
        {
            return "the Governance registry carries a gate with a blank GovernanceId.";
        }

        var duplicates = registry.Gates
            .GroupBy(g => g.GovernanceId, StringComparer.Ordinal)
            .Where(grp => grp.Count() > 1)
            .Select(grp => grp.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        if (duplicates.Length != 0)
        {
            return $"the Governance registry declares duplicate GovernanceId(s): {string.Join(", ", duplicates)}.";
        }

        return null;
    }

    private static void TryRemoveStaging(string stagingPath)
    {
        try
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
        catch (IOException)
        {
            // Best effort. A leftover staging file is inert: it is not the published document.
        }
    }
}
