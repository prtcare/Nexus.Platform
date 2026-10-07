using System.Text;
using System.Text.Json;
using Nexus.ProductCore.Contracts.ReadModel;

namespace Nexus.ProductCore.Core.ReadModel;

/// <summary>What a publication attempt did, in one value a caller can act on.</summary>
public sealed record PlatformRuntimePublishOutcome(
    bool Published,
    string? Path,
    string PayloadDigest,
    string Reason)
{
    public static PlatformRuntimePublishOutcome Refused(string reason, string digest = "") =>
        new(false, null, digest, reason);
}

/// <summary>
/// W10.6 TASK 13 — <b>publishes the Platform runtime read model atomically, to an explicit
/// destination.</b>
///
/// <para>
/// The rules are the ones W10.1 and W10.4 proved for the other two authorities, reused rather than
/// re-derived: an explicit destination, staging beside it and moving over it, refusals that replace
/// nothing, and a semantic digest over the payload alone so a metadata refresh is distinguishable
/// from a state change.
/// </para>
///
/// <para>
/// <b>The commit boundary is the HARDENED one.</b> It is deliberately duplicated rather than shared —
/// these assemblies have zero references by construction — and it carries both W10.4 remediation
/// passes: a unique scratch path per attempt-run, so two concurrent publishers to one destination
/// cannot collide on a fixed <c>.staging</c> name, and a bounded budget rather than a flat attempt
/// count, because the first version exhausted its attempts in ~216 ms while most of the contention
/// window remained.
/// </para>
///
/// <para>
/// <b>Read-only against every authority.</b> The observation reads a repository and one kernel lock
/// probe. It takes no lock, acquires no reservation, and starts nothing. A publisher that needed the
/// writer lock to describe the estate would be claiming the right to change it in order to report on
/// it.
/// </para>
/// </summary>
public sealed class PlatformRuntimeReadPublisher
{
    private readonly PlatformRuntimeObservation _observation;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public PlatformRuntimeReadPublisher(PlatformRuntimeObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _observation = observation;
    }

    /// <summary>The digest a previous publication produced, or null when nothing is published.</summary>
    public static string? PublishedDigest(string destinationDirectory)
    {
        var path = Path.Combine(destinationDirectory, PlatformRuntimeReadContract.FileName);
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

    /// <summary>Observes, validates and publishes. <paramref name="observedAt"/> is supplied so a run
    /// is reproducible for a fixed input and a fixed instant.</summary>
    public PlatformRuntimePublishOutcome Publish(string destinationDirectory, string observedAt, string? sourceRevision = null)
    {
        var destinationCheck = ValidateDestination(destinationDirectory);
        if (destinationCheck is not null)
        {
            return PlatformRuntimePublishOutcome.Refused(destinationCheck);
        }

        PlatformRuntimeReadPayload payload;
        try
        {
            payload = _observation.Observe(observedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsupportedSchemaException)
        {
            return PlatformRuntimePublishOutcome.Refused($"the Platform runtime observation could not be taken: {ex.Message}");
        }

        var digest = PlatformRuntimeObservation.SemanticDigest(payload);

        var model = new PlatformRuntimeReadModel(
            PlatformRuntimeReadContract.SchemaVersion,
            new PlatformRuntimeReadSource(
                PlatformRuntimeReadContract.SchemaVersion,
                PlatformRuntimeReadContract.Authority,
                sourceRevision ?? "(unspecified)",
                sourceRevision ?? "(unspecified)",
                observedAt,
                observedAt,
                digest),
            payload);

        var refusal = Validate(model);
        if (refusal is not null)
        {
            return PlatformRuntimePublishOutcome.Refused(refusal, digest);
        }

        // Serialised ONCE. Every retry below commits these exact bytes.
        var json = JsonSerializer.Serialize(model, WriteOptions);
        var finalPath = Path.Combine(destinationDirectory, PlatformRuntimeReadContract.FileName);

        return CommitAtomically(finalPath, json, digest, out var commitReason)
            ? new PlatformRuntimePublishOutcome(true, finalPath, digest, "published")
            : PlatformRuntimePublishOutcome.Refused(commitReason, digest);
    }

    // ================================================================ the commit boundary

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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> CommitGates =
        new(StringComparer.OrdinalIgnoreCase);

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

                    // A destination that is a DIRECTORY can never be moved over, and Windows raises the
                    // SAME ERROR_ACCESS_DENIED for it as for a transient open handle. The exception type
                    // is a hint; the filesystem state is the evidence.
                    if (Directory.Exists(finalPath))
                    {
                        reason = $"publication refused: '{finalPath}' is a directory. A file cannot be moved "
                               + "over a directory on any filesystem, so this is permanent and is not retried.";
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

    private static bool IsTransientCommitContention(Exception ex)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
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
            return $"'{destinationDirectory}' is not an absolute path. A relative destination resolves "
                 + "against the process's working directory, so two runs could publish to two places and "
                 + "neither would say so.";
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

    /// <summary>
    /// The contract's own preconditions, checked before anything is written.
    ///
    /// <para>
    /// <b>The one rule here that is not shared with the other two publishers:</b> a unit whose
    /// applicability says it has no runtime must NOT carry a runtime state, and a health value must
    /// never appear without an authority that licensed it. Both are checked on the way out so they
    /// cannot be true only in the observer's intentions.
    /// </para>
    /// </summary>
    private static string? Validate(PlatformRuntimeReadModel model)
    {
        if (!string.Equals(model.SchemaVersion, PlatformRuntimeReadContract.SchemaVersion, StringComparison.Ordinal))
        {
            return $"the observation declared contract version '{model.SchemaVersion}', which is not "
                 + $"'{PlatformRuntimeReadContract.SchemaVersion}'.";
        }

        if (string.IsNullOrWhiteSpace(model.Source.SourceId))
        {
            return "the observation does not identify its source.";
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

        var notApplicable = PlatformRuntimeApplicability.LibraryOnly.ToString();
        var notApplicableProcess = PlatformRuntimeProcessState.NotApplicable.ToString();

        foreach (var unit in model.Payload.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.RuntimeUnitId))
            {
                return "a runtime unit was published with no RuntimeUnitId.";
            }

            // TASK 9, enforced rather than intended: a library must not carry a process state.
            if (string.Equals(unit.RuntimeApplicability, notApplicable, StringComparison.Ordinal)
                && !string.Equals(unit.ProcessState, notApplicableProcess, StringComparison.Ordinal))
            {
                return $"'{unit.RuntimeUnitId}' is LibraryOnly but reports ProcessState='{unit.ProcessState}'. "
                     + "A component with no entry point has no process state, and reporting one states a "
                     + "fault that cannot exist.";
            }

            var healthy = string.Equals(unit.HealthState, PlatformRuntimeHealthState.Healthy.ToString(), StringComparison.Ordinal);
            if (healthy && string.Equals(unit.HealthAuthority, PlatformRuntimeHealthAuthority.None.ToString(), StringComparison.Ordinal))
            {
                return $"'{unit.RuntimeUnitId}' reports Healthy with HealthAuthority='None'. A health value "
                     + "with nothing licensing it is an assertion nobody made.";
            }
        }

        var ids = model.Payload.Units
            .GroupBy(u => u.RuntimeUnitId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        if (ids.Length != 0)
        {
            return $"duplicate RuntimeUnitId(s): {string.Join(", ", ids)}.";
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

/// <summary>Raised when the governed repository cannot be read as a solution.</summary>
public sealed class UnsupportedSchemaException : Exception
{
    public UnsupportedSchemaException(string message) : base(message) { }
}
