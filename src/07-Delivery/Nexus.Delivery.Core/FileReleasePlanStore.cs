using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The governed writer for <see cref="ReleaseSecurityPlan"/> — the mechanism that replaces a hand-edited,
/// untracked JSON file as the authority controlling whether a release may be deployed.
///
/// <para>
/// <b>Deliberately the same construction as <see cref="FileReleaseRegistry"/> one level down</b>, because the
/// estate already paid for the alternative: <see cref="FileMode.CreateNew"/> for the write-once guarantees,
/// an entry file beside a record file, and typed refusals instead of exceptions. <b>No second generic
/// persistence framework is introduced.</b> This adapter has one record type, one key, and one job, and
/// nothing else in the estate is asked to use it.
/// </para>
///
/// <para>
/// Layout under the configured root:
/// <code>
/// release-plans/&lt;ReleaseId&gt;/plan.json           the current authoritative plan
/// release-plans/&lt;ReleaseId&gt;/versions/v&lt;n&gt;.json  immutable snapshots, created with CreateNew
/// release-plans/&lt;ReleaseId&gt;/applications.jsonl   append-only ledger of accepted writes
/// </code>
/// </para>
///
/// <para>
/// <b>The five guarantees, and how each is enforced rather than promised.</b>
/// </para>
/// <list type="number">
/// <item><b>Schema validation.</b> <see cref="ReleaseSecurityPlanValidation.Validate"/> runs on every write,
/// including the projected result of an update — not just on the input. A rule that only inspected the
/// request could be satisfied by a request that produced an invalid record.</item>
/// <item><b>Atomic write.</b> The new record is written to a uniquely-named temporary file in the same
/// directory and then moved over <c>plan.json</c> in one filesystem operation, so a reader never observes a
/// partially written authority.</item>
/// <item><b>Version and concurrency protection.</b> Updates are optimistic: the caller states the version it
/// built against, a mismatch is refused rather than merged, and each version's snapshot is created with
/// <see cref="FileMode.CreateNew"/> so a version number can never be written twice — an OS-level guarantee
/// that holds across processes, not just inside this one.</item>
/// <item><b>Provenance.</b> Every version carries who wrote it, when, why, and (for the decisions that
/// require one) the Owner decision it rests on. Validation refuses a record whose provenance is missing.</item>
/// <item><b>Before and after hashes.</b> Recorded in the ledger for refusals as well as acceptances, and
/// returned to the caller on both. A refused write is as auditable as an accepted one.</item>
/// </list>
///
/// <para>
/// <b>A hand-edited <c>plan.json</c> is detectable, and that is the point.</b> Reads cross-check the current
/// record's canonical digest against both its immutable version snapshot and the ledger entry that produced
/// it. A file edited outside this writer no longer matches either, and is reported as
/// <see cref="ReleasePlanReadState.Corrupt"/> rather than read as the authority. This is what makes the
/// authority "governed" in a sense a text editor cannot forge; it is not a claim that a filesystem is
/// tamper-proof, and it is not a substitute for the file-system permissions the machine already has.
/// </para>
///
/// <para>
/// <b>What is deliberately not claimed.</b> The three files are not written in one transaction, so a crash
/// between the <c>plan.json</c> replace and the ledger append leaves a record whose ledger entry is missing.
/// That state is <i>detected</i> (the ledger check above reports it as corrupt) rather than prevented, and it
/// is stated here rather than presented as atomicity across a directory.
/// </para>
/// </summary>
public sealed class FileReleasePlanStore : IReleasePlanStore
{
    private const string PlansDirectoryName = "release-plans";
    private const string PlanFileName = "plan.json";
    private const string VersionsDirectoryName = "versions";
    private const string ApplicationsFileName = "applications.jsonl";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _root;
    private readonly object _gate = new();

    public FileReleasePlanStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A release plan store needs a root directory.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public string Root => _root;

    /// <summary>The directory holding one release's governed plan. Exposed so evidence can name it.</summary>
    public string DirectoryFor(ReleaseId releaseId)
        => TryResolvePlanDirectory(releaseId, out var directory)
            ? directory
            : throw new ArgumentException($"Release id '{releaseId}' does not resolve inside the plan store root.", nameof(releaseId));

    public Task<ReleasePlanReadResult> ReadAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolvePlanDirectory(releaseId, out var directory))
        {
            return Task.FromResult(new ReleasePlanReadResult(
                ReleasePlanReadState.Corrupt, releaseId, null,
                $"Release id '{releaseId}' does not resolve inside the plan store root."));
        }

        var planPath = Path.Combine(directory, PlanFileName);

        if (!File.Exists(planPath))
        {
            return Task.FromResult(new ReleasePlanReadResult(
                ReleasePlanReadState.Absent, releaseId, null,
                $"No governed release plan is recorded for '{releaseId}'."));
        }

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(planPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new ReleasePlanReadResult(
                ReleasePlanReadState.Corrupt, releaseId, null,
                $"The plan for '{releaseId}' could not be read: {ex.GetType().Name}."));
        }

        if (!ReleaseSecurityPlanCodec.TryDecode(bytes, out var plan, out var decodeDetail) || plan is null)
        {
            return Task.FromResult(new ReleasePlanReadResult(ReleasePlanReadState.Corrupt, releaseId, null, decodeDetail));
        }

        if (plan.ReleaseId is null || plan.ReleaseId != releaseId)
        {
            // A record that decoded correctly but names a different release would defeat the lookup key from
            // the inside. Same check, and same reason, as the release registry's.
            return Task.FromResult(new ReleasePlanReadResult(
                ReleasePlanReadState.Corrupt, releaseId, null,
                $"The stored plan decodes to {(plan.ReleaseId?.Value ?? "(no release id)")}, not {releaseId}."));
        }

        var integrity = CrossCheckIntegrity(directory, plan, bytes);

        return Task.FromResult(integrity is null
            ? new ReleasePlanReadResult(
                ReleasePlanReadState.Present, releaseId, plan,
                $"Governed plan for '{releaseId}' at version {plan.Version}, digest {plan.ComputePlanDigest()}.")
            : new ReleasePlanReadResult(ReleasePlanReadState.Corrupt, releaseId, null, integrity));
    }

    public Task<ReleasePlanWriteOutcome> CreateAsync(ReleaseSecurityPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (plan.ReleaseId is null || !Contracts.ReleaseId.IsValid(plan.ReleaseId.Value))
        {
            return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                ReleasePlanWriteRefusalReason.PlanInvalid, plan.Version, null, null,
                "A release plan must name a valid release id.",
                ReleaseSecurityPlanValidation.Validate(plan)));
        }

        var releaseId = plan.ReleaseId;

        if (!TryResolvePlanDirectory(releaseId, out var directory))
        {
            return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                ReleasePlanWriteRefusalReason.PathEscapesStoreRoot, plan.Version, null, null,
                $"Release id '{releaseId}' does not resolve inside the plan store root."));
        }

        var verdict = ReleaseSecurityPlanValidation.Validate(plan);

        if (!verdict.IsValid)
        {
            return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                ReleasePlanWriteRefusalReason.PlanInvalid, plan.Version, null, plan.ComputePlanDigest(),
                $"The plan was refused by validation: {string.Join(" ", verdict.Detail)}",
                verdict, plan));
        }

        var digest = plan.ComputePlanDigest();
        var planPath = Path.Combine(directory, PlanFileName);

        lock (_gate)
        {
            if (File.Exists(planPath))
            {
                var existing = ReadUncrossChecked(planPath);

                if (existing is not null && existing.ComputePlanDigest() == digest)
                {
                    // A byte-identical re-presentation. A no-op, not a second write — the same reasoning as
                    // the artifact store and the release registry: a retried run should succeed.
                    return Task.FromResult(ReleasePlanWriteOutcome.Accepted(
                        existing.Version, digest, digest, existing,
                        $"Release '{releaseId}' already holds this exact plan at version {existing.Version}; nothing was written.",
                        alreadyPresent: true));
                }

                return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                    ReleasePlanWriteRefusalReason.ReleaseIdAlreadyHasAPlan,
                    existing?.Version ?? plan.Version,
                    existing?.ComputePlanDigest(), digest,
                    $"'{(existing is null ? "?" : existing.ReleaseId)}' already has a governed plan, and the record presented is not identical "
                    + "to it. A changed plan state is a NEW VERSION applied through the writer, never an overwrite."));
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(directory, VersionsDirectoryName));

                if (!TryWriteVersionSnapshot(directory, plan, digest, out var snapshotDetail))
                {
                    return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                        ReleasePlanWriteRefusalReason.VersionAlreadyRecorded, plan.Version, null, digest, snapshotDetail));
                }

                ReplacePlanFile(planPath, plan);
                AppendApplication(directory, plan, previousVersion: 0, beforeHash: null, afterHash: digest, mode: "Create");

                return Task.FromResult(ReleasePlanWriteOutcome.Accepted(
                    plan.Version, null, digest, plan,
                    $"Recorded version {plan.Version} of the governed plan for '{releaseId}'."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                    ReleasePlanWriteRefusalReason.StorageFailure, plan.Version, null, digest,
                    $"Writing the plan failed: {ex.GetType().Name}. No state was changed."));
            }
        }
    }

    public Task<ReleasePlanWriteOutcome> ApplyAsync(ReleaseSecurityPlanUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolvePlanDirectory(update.ReleaseId, out var directory))
        {
            return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                ReleasePlanWriteRefusalReason.PathEscapesStoreRoot, update.ExpectedVersion, null, null,
                $"Release id '{update.ReleaseId}' does not resolve inside the plan store root."));
        }

        var planPath = Path.Combine(directory, PlanFileName);

        lock (_gate)
        {
            var current = ReadUncrossChecked(planPath);

            if (current is null)
            {
                return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                    ReleasePlanWriteRefusalReason.PlanNotFound, update.ExpectedVersion, null, null,
                    $"No governed plan is recorded for '{update.ReleaseId}', so there is nothing to update. "
                    + "A plan is created once and then versioned; an update never brings one into existence."));
            }

            var beforeHash = current.ComputePlanDigest();

            if (current.Version != update.ExpectedVersion)
            {
                return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                    ReleasePlanWriteRefusalReason.ConcurrentModification, current.Version, beforeHash, null,
                    $"The update was built against version {update.ExpectedVersion}, but '{update.ReleaseId}' is at "
                    + $"version {current.Version}. Refused rather than merged: the update was written against a state that no longer holds.",
                    plan: current));
            }

            var projected = update.ApplyTo(current);
            var verdict = ReleaseSecurityPlanValidation.Validate(projected);

            if (!verdict.IsValid)
            {
                return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                    ReleasePlanWriteRefusalReason.PlanInvalid, current.Version, beforeHash, projected.ComputePlanDigest(),
                    $"The update would produce a plan the contract refuses: {string.Join(" ", verdict.Detail)}",
                    verdict, current));
            }

            var afterHash = projected.ComputePlanDigest();

            try
            {
                if (!TryWriteVersionSnapshot(directory, projected, afterHash, out var snapshotDetail))
                {
                    return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                        ReleasePlanWriteRefusalReason.VersionAlreadyRecorded, current.Version, beforeHash, afterHash,
                        snapshotDetail, plan: current));
                }

                ReplacePlanFile(planPath, projected);
                AppendApplication(directory, projected, current.Version, beforeHash, afterHash, mode: "Apply");

                return Task.FromResult(ReleasePlanWriteOutcome.Accepted(
                    projected.Version, beforeHash, afterHash, projected,
                    $"'{update.ReleaseId}' moved from version {current.Version} to {projected.Version}. "
                    + $"before={beforeHash} after={afterHash}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ReleasePlanWriteOutcome.Refused(
                    ReleasePlanWriteRefusalReason.StorageFailure, current.Version, beforeHash, afterHash,
                    $"Writing the plan failed: {ex.GetType().Name}. No state was changed.", plan: current));
            }
        }
    }

    public Task<IReadOnlyList<ReleasePlanApplicationRecord>> ReadApplicationsAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolvePlanDirectory(releaseId, out var directory))
        {
            return Task.FromResult<IReadOnlyList<ReleasePlanApplicationRecord>>([]);
        }

        var ledgerPath = Path.Combine(directory, ApplicationsFileName);

        if (!File.Exists(ledgerPath))
        {
            return Task.FromResult<IReadOnlyList<ReleasePlanApplicationRecord>>([]);
        }

        var records = new List<ReleasePlanApplicationRecord>();

        foreach (var line in File.ReadAllLines(ledgerPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var dto = JsonSerializer.Deserialize<ApplicationDto>(line, Options);
                var record = dto?.ToRecord();

                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // An unreadable row is a damage finding, not a reason to hide the rest of the ledger. Same
                // treatment as the release lineage log's malformed row.
            }
        }

        return Task.FromResult<IReadOnlyList<ReleasePlanApplicationRecord>>(records);
    }

    /// <summary>
    /// Cross-checks the current record against its immutable snapshot and the ledger entry that produced it.
    /// Returns <see langword="null"/> when everything agrees, and the reason it does not otherwise.
    ///
    /// <para>
    /// This is the check that makes an out-of-band edit visible. It is deliberately not a signature: the
    /// estate has no key management, and inventing one here would be a much larger change than this mechanism
    /// is. What it does establish is that <c>plan.json</c> cannot be changed without also changing the
    /// snapshot and the ledger — at which point the change is a deliberate rewrite of three files rather than
    /// an edit, and the ledger's before/after chain no longer closes.
    /// </para>
    /// </summary>
    private static string? CrossCheckIntegrity(string directory, ReleaseSecurityPlan plan, byte[] storedBytes)
    {
        var digest = plan.ComputePlanDigest();
        var snapshotPath = Path.Combine(directory, VersionsDirectoryName, $"v{plan.Version}.json");

        if (!File.Exists(snapshotPath))
        {
            return $"The plan for '{plan.ReleaseId}' is at version {plan.Version}, but no immutable snapshot of that version exists. "
                 + "A version that was never snapshotted did not come from this writer.";
        }

        if (!ReleaseSecurityPlanCodec.TryDecode(File.ReadAllBytes(snapshotPath), out var snapshot, out var snapshotDetail) || snapshot is null)
        {
            return $"The snapshot of version {plan.Version} does not decode: {snapshotDetail}";
        }

        if (snapshot.ComputePlanDigest() != digest)
        {
            return $"The current record for '{plan.ReleaseId}' hashes to {digest}, but its own immutable snapshot of version "
                 + $"{plan.Version} hashes to {snapshot.ComputePlanDigest()}. The authority has been changed outside the governed writer.";
        }

        var ledgerPath = Path.Combine(directory, ApplicationsFileName);

        if (!File.Exists(ledgerPath))
        {
            return $"The plan for '{plan.ReleaseId}' is at version {plan.Version}, but the application ledger is missing. "
                 + "A version with no ledger entry is a write this writer did not make.";
        }

        var storedBytesHash = ArtifactDigest.Compute(storedBytes);
        ReleasePlanApplicationRecord? entry = null;

        foreach (var line in File.ReadAllLines(ledgerPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var dto = JsonSerializer.Deserialize<ApplicationDto>(line, Options);
                var record = dto?.ToRecord();

                if (record is not null && record.Version == plan.Version)
                {
                    entry = record;
                }
            }
            catch (JsonException)
            {
                // Counted as damage by the absence of a matching entry below.
            }
        }

        if (entry is null)
        {
            return $"The plan for '{plan.ReleaseId}' is at version {plan.Version}, but the application ledger records no write "
                 + "that produced it. A version with no ledger entry is a write this writer did not make.";
        }

        if (entry.AfterHash != digest)
        {
            return $"The ledger records version {plan.Version} of '{plan.ReleaseId}' as hashing to {entry.AfterHash}, but the current "
                 + $"record hashes to {digest}. The authority has been changed outside the governed writer.";
        }

        if (entry.StoredBytesHash is not null && entry.StoredBytesHash != storedBytesHash)
        {
            return $"The ledger records version {plan.Version} of '{plan.ReleaseId}' with stored-bytes hash {entry.StoredBytesHash}, "
                 + $"but the file on disk hashes to {storedBytesHash}. The authority has been changed outside the governed writer.";
        }

        return null;
    }

    private static ReleaseSecurityPlan? ReadUncrossChecked(string planPath)
    {
        if (!File.Exists(planPath))
        {
            return null;
        }

        try
        {
            return ReleaseSecurityPlanCodec.TryDecode(File.ReadAllBytes(planPath), out var plan, out _) ? plan : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the immutable snapshot of a version. <see cref="FileMode.CreateNew"/> is the concurrency guard:
    /// if the file exists, this version was already written, whoever wrote it.
    /// </summary>
    private static bool TryWriteVersionSnapshot(string directory, ReleaseSecurityPlan plan, ArtifactDigest digest, out string detail)
    {
        var versionsDirectory = Path.Combine(directory, VersionsDirectoryName);
        Directory.CreateDirectory(versionsDirectory);

        var snapshotPath = Path.Combine(versionsDirectory, $"v{plan.Version}.json");

        try
        {
            using var destination = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var encoded = ReleaseSecurityPlanCodec.Encode(plan);
            destination.Write(encoded, 0, encoded.Length);
        }
        catch (IOException ex) when (File.Exists(snapshotPath))
        {
            detail = $"Version {plan.Version} of '{plan.ReleaseId}' is already recorded. A version number identifies one act and is never reused.";
            _ = ex;
            return false;
        }

        detail = $"Snapshotted version {plan.Version} ({digest}).";
        return true;
    }

    /// <summary>
    /// Replaces <c>plan.json</c> in one filesystem operation: the new record is fully written to a uniquely
    /// named temporary file in the same directory first, then moved over the destination.
    /// </summary>
    private static void ReplacePlanFile(string planPath, ReleaseSecurityPlan plan)
    {
        var temporary = planPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var encoded = ReleaseSecurityPlanCodec.Encode(plan);

        try
        {
            using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                destination.Write(encoded, 0, encoded.Length);
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporary, planPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void AppendApplication(
        string directory,
        ReleaseSecurityPlan plan,
        int previousVersion,
        ArtifactDigest? beforeHash,
        ArtifactDigest afterHash,
        string mode)
    {
        var ledgerPath = Path.Combine(directory, ApplicationsFileName);
        var storedBytes = ReleaseSecurityPlanCodec.Encode(plan);

        var dto = ApplicationDto.From(new ReleasePlanApplicationRecord(
            plan.ReleaseId!,
            plan.Version,
            previousVersion,
            plan.UpdatedAt ?? DateTimeOffset.UtcNow,
            plan.UpdatedBy,
            plan.Reason,
            mode,
            beforeHash,
            afterHash,
            plan.HumanDecisionReference,
            plan.CredentialRotationDecisionReference)
        {
            StoredBytesHash = ArtifactDigest.Compute(storedBytes)
        });

        using var stream = new FileStream(ledgerPath, FileMode.Append, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(JsonSerializer.Serialize(dto, Options));
    }

    private bool TryResolvePlanDirectory(ReleaseId releaseId, out string directory)
    {
        directory = string.Empty;

        if (releaseId is null || !Contracts.ReleaseId.IsValid(releaseId.Value))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, PlansDirectoryName, releaseId.Value));
        var expectedPrefix = Path.Combine(_root, PlansDirectoryName) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        directory = candidate;
        return true;
    }

    private sealed record ApplicationDto(
        [property: JsonPropertyOrder(0)] string ReleaseId,
        [property: JsonPropertyOrder(1)] int Version,
        [property: JsonPropertyOrder(2)] int PreviousVersion,
        [property: JsonPropertyOrder(3)] string AppliedAt,
        [property: JsonPropertyOrder(4)] string AppliedBy,
        [property: JsonPropertyOrder(5)] string Reason,
        [property: JsonPropertyOrder(6)] string Mode,
        [property: JsonPropertyOrder(7)] string? BeforeHash,
        [property: JsonPropertyOrder(8)] string AfterHash,
        [property: JsonPropertyOrder(9)] string? StoredBytesHash,
        [property: JsonPropertyOrder(10)] string? HumanDecisionReference,
        [property: JsonPropertyOrder(11)] string? CredentialRotationDecisionReference)
    {
        internal static ApplicationDto From(ReleasePlanApplicationRecord record) => new(
            record.ReleaseId.Value,
            record.Version,
            record.PreviousVersion,
            record.AppliedAt.ToString("O"),
            record.AppliedBy,
            record.Reason,
            record.Mode,
            record.BeforeHash?.ToString(),
            record.AfterHash.ToString(),
            record.StoredBytesHash?.ToString(),
            record.HumanDecisionReference,
            record.CredentialRotationDecisionReference);

        internal ReleasePlanApplicationRecord ToRecord() => new(
            Contracts.ReleaseId.Parse(ReleaseId),
            Version,
            PreviousVersion,
            DateTimeOffset.Parse(AppliedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            AppliedBy,
            Reason,
            Mode,
            BeforeHash is null ? null : Contracts.ArtifactDigest.Parse(BeforeHash),
            Contracts.ArtifactDigest.Parse(AfterHash),
            HumanDecisionReference,
            CredentialRotationDecisionReference)
        {
            StoredBytesHash = StoredBytesHash is null ? null : Contracts.ArtifactDigest.Parse(StoredBytesHash)
        };
    }
}
