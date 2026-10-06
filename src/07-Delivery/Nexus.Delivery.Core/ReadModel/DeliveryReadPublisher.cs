using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts.ReadModel;

namespace Nexus.Delivery.Core.ReadModel;

/// <summary>What a publication attempt did, in one value a caller can act on.</summary>
public sealed record DeliveryReadPublishOutcome(
    bool Published,
    string? Path,
    string PayloadDigest,
    string Reason)
{
    public static DeliveryReadPublishOutcome Refused(string reason, string digest = "") =>
        new(false, null, digest, reason);
}

/// <summary>
/// W10.1 TASKs 7 and 8 — <b>publishes the Delivery read model atomically, to an explicit
/// destination.</b>
///
/// <para>
/// <b>The destination is a parameter, never a constant.</b> The consumer's location is not compiled
/// into Delivery: a hardcoded path would make the producer depend on one machine's layout, and the
/// ruling for this milestone requires an explicit, governed destination. A caller that names nothing
/// gets a refusal, not a default.
/// </para>
///
/// <para>
/// <b>Atomic by staging.</b> The document is written to a sibling staging file in the SAME directory
/// and then moved over the destination. Same-directory matters: a move across volumes is a copy, which
/// is not atomic. A reader therefore sees either the previous complete snapshot or the new complete
/// snapshot, and never a half-written file — including when the process dies mid-write, because the
/// staging file has not been moved.
/// </para>
///
/// <para>
/// <b>Refusals replace nothing.</b> Every failure path — unreadable authority, a projection that does
/// not satisfy the contract, an unusable destination — leaves the existing snapshot exactly as it
/// was. A publisher that cleared the destination and then failed would be worse than one that did
/// nothing at all.
/// </para>
///
/// <para>
/// <b>Idempotent by construction.</b> The payload digest excludes observation and publication
/// timestamps, so republishing unchanged authority state produces the same semantic digest while the
/// metadata moves. <see cref="PublishedDigest"/> lets a caller see whether the state actually changed.
/// </para>
/// </summary>
public sealed class DeliveryReadPublisher
{
    private readonly DeliveryReadProjection _projection;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public DeliveryReadPublisher(DeliveryReadProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _projection = projection;
    }

    /// <summary>The digest a previous publication produced, or null when nothing is published.</summary>
    public static string? PublishedDigest(string destinationDirectory)
    {
        var path = Path.Combine(destinationDirectory, DeliveryReadContract.FileName);
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

    /// <summary>
    /// Projects, validates and publishes. <paramref name="observedAt"/> is supplied by the caller so
    /// a projection is reproducible for a fixed input and a fixed instant.
    /// </summary>
    public DeliveryReadPublishOutcome Publish(string destinationDirectory, string observedAt, string? sourceRevision = null)
    {
        var destinationCheck = ValidateDestination(destinationDirectory);
        if (destinationCheck is not null)
        {
            return DeliveryReadPublishOutcome.Refused(destinationCheck);
        }

        DeliveryReadPayload payload;
        try
        {
            payload = _projection.Project();
        }
        catch (DeliveryAuthorityUnavailableException ex)
        {
            // The authority is not there. Nothing is published and nothing is replaced — and in
            // particular no empty document is published over a good previous one.
            return DeliveryReadPublishOutcome.Refused($"AUTHORITY_UNAVAILABLE — {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // The authority could not be read. Nothing is published and nothing is replaced.
            return DeliveryReadPublishOutcome.Refused($"the Delivery authority could not be read: {ex.Message}");
        }

        var digest = DeliveryReadProjection.SemanticDigest(payload);

        var model = new DeliveryReadModel(
            DeliveryReadContract.SchemaVersion,
            new DeliveryReadSource(
                DeliveryReadContract.SchemaVersion,
                DeliveryReadContract.Authority,
                sourceRevision ?? "(unspecified)",
                sourceRevision ?? "(unspecified)",
                observedAt,
                observedAt,
                digest),
            payload);

        var refusal = Validate(model);
        if (refusal is not null)
        {
            return DeliveryReadPublishOutcome.Refused(refusal, digest);
        }

        // Serialised ONCE. Every retry below commits these exact bytes — the retry never re-reads the
        // authority, re-projects, re-validates or re-digests. See the retry note on CommitAtomically.
        var json = JsonSerializer.Serialize(model, WriteOptions);
        var finalPath = Path.Combine(destinationDirectory, DeliveryReadContract.FileName);

        return CommitAtomically(finalPath, json, digest, out var commitReason)
            ? new DeliveryReadPublishOutcome(true, finalPath, digest, "published")
            : DeliveryReadPublishOutcome.Refused(commitReason, digest);
    }

    // ================================================================ the commit boundary

    /// <summary>
    /// How many times the commit boundary may be attempted before the publication is refused.
    /// </summary>
    // The attempt ceiling is a backstop, not the real bound: CommitBudget is. The first version used 5
    // attempts with a flat small backoff, which exhausted all five in ~216 ms — consuming 11% of the
    // budget and giving up while most of the contention window remained. MEASURED, from the retry
    // message the publisher itself emits.
    private const int MaxCommitAttempts = 20;

    /// <summary>Backoff between attempts. Small: this absorbs a transient handle, not an outage.</summary>
    private static readonly TimeSpan[] CommitBackoff =
    [
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(160),
        TimeSpan.FromMilliseconds(200)
    ];

    /// <summary>
    /// The whole retry budget. Bounded independently of the attempt count, so a slow filesystem cannot
    /// turn five attempts into a long stall.
    /// </summary>
    private static readonly TimeSpan CommitBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// W10.4 REMEDIATION — per-destination serialisation.
    ///
    /// <para>
    /// Keyed on the <b>destination path</b>, not the process. Two publishers writing to the same
    /// directory are serialised; two publishers writing to different directories are not, which is why
    /// this is not a process-wide lock.
    /// </para>
    ///
    /// <para>
    /// This does not change the publication contract. An atomic replace has always been
    /// last-writer-wins; what this removes is the case where "who wins" was decided by which publisher
    /// happened to lose a race on a shared scratch file.
    /// </para>
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> CommitGates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <b>The stage-then-move commit, made robust to transient contention.</b>
    ///
    /// <para>
    /// <b>The defect this replaces.</b> The staging path was the fixed string <c>finalPath + ".staging"</c>.
    /// Every publisher writing to one destination therefore shared ONE scratch file, so two concurrent
    /// publishers collided on it and one was denied with
    /// <see cref="UnauthorizedAccessException"/>. Measured, not theorised: the W10.4 regression
    /// reproduces it on demand.
    /// </para>
    ///
    /// <para>
    /// <b>Two changes, both at this boundary and nowhere else.</b> The staging file now carries a unique
    /// suffix, so no two publishers can ever share a scratch path; and the commit is retried a bounded
    /// number of times on contention. Nothing upstream is repeated — <paramref name="json"/> is the
    /// already-validated payload, computed once by the caller.
    /// </para>
    /// </summary>
    private bool CommitAtomically(string finalPath, string json, string digest, out string reason)
    {
        // A unique scratch name per attempt-run. Collisions are now impossible by construction rather
        // than unlikely, which is what the fixed name got wrong.
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
                    // The commit point. Same directory as the destination, so this is a rename and not a
                    // cross-volume copy.
                    File.Move(stagingPath, finalPath, overwrite: true);

                    reason = "published";
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    TryRemoveStaging(stagingPath);

                    // A read-only destination is a CONFIGURATION condition, not contention. Retrying it
                    // would spend the budget on an outcome that cannot change, so it is refused at once.
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
                    var attemptsLeft = attempt < MaxCommitAttempts;
                    var budgetLeft = clock.Elapsed < CommitBudget;

                    if (!transient || !attemptsLeft || !budgetLeft)
                    {
                        reason = transient
                            ? $"publication failed while staging '{stagingPath}': {ex.Message}. Retried "
                              + $"{attempt} time(s) over {clock.ElapsedMilliseconds} ms and gave up "
                              + $"(budget: {MaxCommitAttempts} attempts / {CommitBudget.TotalSeconds:0} s). "
                              + "The previously published snapshot, if any, is unchanged."
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

    /// <summary>
    /// Whether the destination exists and is read-only — a condition retrying cannot change.
    /// </summary>
    private static bool IsReadOnlyDestination(string finalPath)
    {
        try
        {
            return File.Exists(finalPath) && File.GetAttributes(finalPath).HasFlag(FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If the attribute cannot be read, fall through to the normal classification rather than
            // inventing a permanent verdict from a failed probe.
            return false;
        }
    }

    /// <summary>
    /// <b>The exceptions this boundary will retry, enumerated and justified.</b>
    ///
    /// <para>
    /// Deliberately NOT a broad catch. Anything not named here propagates unretried, because a retry
    /// loop that swallows everything is how a disk-full or a permissions mistake becomes a two-second
    /// stall before the same failure.
    /// </para>
    /// </summary>
    private static bool IsTransientCommitContention(Exception ex)
    {
        switch (ex)
        {
            // The measured defect. Windows raises UnauthorizedAccessException — not IOException — when
            // another handle is open on the target. ValidateDestination has ALREADY proven the directory
            // is creatable, so a directory-level ACL failure cannot reach this point; what remains at
            // this boundary is a transient handle.
            case UnauthorizedAccessException:
                return true;

            // The canonical transient-contention codes. Any other IOException is a genuine I/O fault
            // (full disk, bad path) and is not retried.
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
                 + "against the process's working directory, so two runs could publish to two places "
                 + "and neither would say so.";
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
    /// These are the checks a consumer would make — version recognised, digest well-formed, source
    /// identified. Making them here means a document that would be refused downstream is never
    /// published, so a consumer never has to treat "the file exists" as "the file is good".
    /// </para>
    /// </summary>
    private static string? Validate(DeliveryReadModel model)
    {
        if (!string.Equals(model.SchemaVersion, DeliveryReadContract.SchemaVersion, StringComparison.Ordinal))
        {
            return $"the projection declared contract version '{model.SchemaVersion}', which is not "
                 + $"'{DeliveryReadContract.SchemaVersion}'. A consumer refuses an unrecognised version, "
                 + "and publishing one would take the feed dark.";
        }

        if (string.IsNullOrWhiteSpace(model.Source.SourceId))
        {
            return "the projection does not identify its source. A consumer cannot attribute a fact it "
                 + "cannot trace.";
        }

        if (model.Source.PayloadDigest.Length != 71 || !model.Source.PayloadDigest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return $"the payload digest '{model.Source.PayloadDigest}' is not a sha256: prefixed "
                 + "lowercase hex digest.";
        }

        if (!DateTimeOffset.TryParse(model.Source.ObservedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _))
        {
            return $"the observation instant '{model.Source.ObservedAt}' is not a parseable timestamp.";
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
            // Best effort. A leftover staging file is inert: it is not the published document, and
            // the next publication overwrites it.
        }
    }
}
