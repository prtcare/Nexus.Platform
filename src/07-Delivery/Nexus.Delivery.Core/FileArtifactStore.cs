using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The first <see cref="IArtifactStore"/> adapter: a deterministic, local, file-backed artifact store.
///
/// <para>
/// <b>Same reasoning as W9.1's registry adapter: the least capable thing that satisfies the contract.</b>
/// Nothing about a filesystem may bias the interface, and if it does, that is discovered here for the cost
/// of a test rather than when an OCI or object-storage adapter is written. The Owner's ruling permits a
/// local, file-backed registry for the W9 proof and requires the cloud backends to stay replaceable
/// adapters.
/// </para>
///
/// <para>
/// <b>Immutability is enforced, not assumed.</b> Publishing an <see cref="ArtifactId"/> whose content
/// already exists with different bytes is refused. The bytes themselves are written with
/// <see cref="FileMode.CreateNew"/> — an OS-level atomic create — so two concurrent publishes of the same
/// id cannot both succeed and no caller has to check-then-write and hope nothing raced it.
/// </para>
///
/// <para>
/// Layout under the configured root:
/// <code>
/// artifacts/&lt;unitId&gt;/&lt;type&gt;/&lt;name&gt;@&lt;version&gt;/content.bin    the bytes
/// artifacts/&lt;unitId&gt;/&lt;type&gt;/&lt;name&gt;@&lt;version&gt;/entry.json     identity, provenance, lifecycle
/// builds/&lt;buildId&gt;.index                              artifact ids produced by one build
/// </code>
/// </para>
/// </summary>
public sealed class FileArtifactStore : IArtifactStore
{
    private const string ArtifactsDirectoryName = "artifacts";
    private const string BuildsDirectoryName = "builds";
    private const string ContentFileName = "content.bin";
    private const string EntryFileName = "entry.json";

    /// <summary>
    /// Where the identity bindings live. <b>Outside <c>artifacts/</c> on purpose.</b> The binding must survive
    /// the removal of the payload it describes, and a record stored inside that payload cannot. See
    /// <see cref="ArtifactIdentity"/> for the incident that made this a requirement rather than a preference.
    /// </summary>
    private const string IdentitiesDirectoryName = "identities";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _root;
    private readonly object _gate = new();

    public FileArtifactStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("An artifact store needs a root directory.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public string Root => _root;

    public Task<ArtifactPublishOutcome> PublishAsync(PackagedArtifact artifact, string sourceFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(sourceFilePath))
        {
            return Task.FromResult(ArtifactPublishOutcome.Refused(
                ArtifactPublishRefusalReason.SourceFileMissing,
                $"Nothing to publish at '{Path.GetFileName(sourceFilePath)}'."));
        }

        if (!TryResolveArtifactDirectory(artifact.ArtifactId, out var directory))
        {
            return Task.FromResult(ArtifactPublishOutcome.Refused(
                ArtifactPublishRefusalReason.PathEscapesStoreRoot,
                $"Artifact id '{artifact.ArtifactId}' does not resolve inside the store root."));
        }

        // The bytes are hashed on the way in. A publish that trusted the caller's digest would let an
        // artifact be recorded under a hash it does not have, and every later verification would agree
        // with the wrong answer.
        ArtifactDigest actual;
        long size;
        using (var stream = File.OpenRead(sourceFilePath))
        {
            actual = ArtifactDigest.Compute(stream);
            size = stream.Length;
        }

        if (actual != artifact.ContentDigest)
        {
            return Task.FromResult(ArtifactPublishOutcome.Refused(
                ArtifactPublishRefusalReason.ContentHashMismatch,
                $"The bytes hash to {actual}, but the artifact claims {artifact.ContentDigest}."));
        }

        lock (_gate)
        {
            var entryPath = Path.Combine(directory, EntryFileName);
            var contentPath = Path.Combine(directory, ContentFileName);

            // ---- the identity binding, consulted BEFORE the payload --------------------------------------
            // This is the W9.6 control. The payload directory is what a store normally reads to decide
            // whether an id is taken, and it is also the thing a lane deletes when it wants to discard a
            // failed build - so a missing payload used to read as a free id. The identity record is written
            // once, lives outside the payload, and is never removed with it, so "was this id ever used?" is
            // answerable after the payload is gone.
            var identity = ReadIdentity(artifact.ArtifactId);

            if (identity is not null)
            {
                if (identity.IsWithdrawn)
                {
                    RecordAttempt(artifact.ArtifactId, actual, accepted: false, "withdrawn");

                    return Task.FromResult(ArtifactPublishOutcome.Refused(
                        ArtifactPublishRefusalReason.ArtifactIdWithdrawn,
                        $"'{artifact.ArtifactId}' was withdrawn by '{identity.WithdrawnBy}' and is never reusable: "
                        + $"{identity.WithdrawnReason}. A withdrawn version is spent - publish a new version, which "
                        + "is what makes the version number an honest count of how many payloads have occupied this id."));
                }

                if (!identity.Holds(actual))
                {
                    RecordAttempt(artifact.ArtifactId, actual, accepted: false, "different-payload");

                    return Task.FromResult(ArtifactPublishOutcome.Refused(
                        ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes,
                        $"'{artifact.ArtifactId}' is bound to {identity.ContentDigest} and this payload is {actual}. "
                        + "Changed bytes always mean a NEW build, never a re-publish under the same id. "
                        + (File.Exists(entryPath)
                            ? string.Empty
                            : "The payload directory is absent, so the binding was read from the identity record "
                              + "rather than from the artifact itself - removing the bytes does not free the id.")));
                }
            }

            if (File.Exists(entryPath))
            {
                var existing = ReadEntry(entryPath);
                if (existing is not null && existing.ContentDigest != actual)
                {
                    return Task.FromResult(ArtifactPublishOutcome.Refused(
                        ArtifactPublishRefusalReason.ArtifactIdExistsWithDifferentBytes,
                        $"'{artifact.ArtifactId}' already holds {existing.ContentDigest}. "
                        + "Changed bytes always mean a NEW build, never a re-publish under the same id."));
                }

                // Identical content already present. Repair the bytes if they went missing, then report a
                // no-op — a retried pipeline run should succeed, not fail for having already succeeded.
                if (!File.Exists(contentPath))
                {
                    File.Copy(sourceFilePath, contentPath);
                }

                return Task.FromResult(ArtifactPublishOutcome.Accepted(
                    existing ?? BuildEntry(artifact, actual, size),
                    alreadyPresent: true));
            }

            try
            {
                Directory.CreateDirectory(directory);

                // CreateNew is the write-once guarantee at the byte level.
                using (var destination = new FileStream(contentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var source = File.OpenRead(sourceFilePath))
                {
                    source.CopyTo(destination);
                }

                var entry = BuildEntry(artifact, actual, size);
                File.WriteAllText(entryPath, JsonSerializer.Serialize(EntryDto.From(entry), Options), Encoding.UTF8);
                AppendToBuildIndex(artifact.BuildId, artifact.ArtifactId);

                // The binding is written whenever it is missing, not only on a first publish: a store whose
                // payloads predate this record - every store in the estate when W9.6 began - acquires its
                // bindings by re-publishing bytes it already holds, which the idempotent path above allows.
                // Without this, an existing artifact would be protected only until the day someone deleted it.
                if (identity is null)
                {
                    WriteIdentity(new ArtifactIdentity
                    {
                        SchemaVersion = ArtifactIdentity.CurrentSchema,
                        ArtifactId = artifact.ArtifactId,
                        ContentDigest = actual,
                        BuildId = artifact.BuildId,
                        FirstAcceptedUtc = DateTimeOffset.UtcNow,
                        State = ArtifactIdentityState.Active,
                        PublishAttempts = 1
                    });
                }
                else
                {
                    RecordAttempt(artifact.ArtifactId, actual, accepted: true, "repaired-payload");
                }

                // alreadyPresent is answered by the IDENTITY, not by whether a directory happened to survive.
                // The question the member asks is "did this id already hold these bytes?", and after a payload
                // deletion the identity record is the only thing that can answer it. Reporting false here
                // would say "this is new content", which is the one thing it demonstrably is not.
                return Task.FromResult(ArtifactPublishOutcome.Accepted(entry, alreadyPresent: identity is not null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ArtifactPublishOutcome.Refused(
                    ArtifactPublishRefusalReason.StorageFailure,
                    $"Writing the artifact failed: {ex.GetType().Name}."));
            }
        }
    }

    public async Task<ArtifactFetchOutcome> FetchAsync(ArtifactId artifactId, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactId);

        var entry = await ResolveByArtifactIdAsync(artifactId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return ArtifactFetchOutcome.Absent();
        }

        if (entry.Lifecycle == ArtifactLifecycleState.Quarantined)
        {
            return ArtifactFetchOutcome.Quarantined(entry.LifecycleReason ?? "Quarantined.");
        }

        if (!TryResolveArtifactDirectory(artifactId, out var directory))
        {
            return ArtifactFetchOutcome.Absent();
        }

        var contentPath = Path.Combine(directory, ContentFileName);
        if (!File.Exists(contentPath))
        {
            return ArtifactFetchOutcome.Corrupt($"'{artifactId}' is recorded but its content is missing from the store.");
        }

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(contentPath, destinationPath, overwrite: true);

        var verification = await VerifyHashAsync(artifactId, entry.ContentDigest, cancellationToken).ConfigureAwait(false);

        return verification.IsMatch
            ? ArtifactFetchOutcome.Fetched()
            : ArtifactFetchOutcome.Corrupt(verification.Detail);
    }

    public async Task<bool> ExistsAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
        => await ResolveByArtifactIdAsync(artifactId, cancellationToken).ConfigureAwait(false) is not null;

    public Task<ArtifactHashVerification> VerifyHashAsync(ArtifactId artifactId, ArtifactDigest expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactId);
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveArtifactDirectory(artifactId, out var directory))
        {
            return Task.FromResult(new ArtifactHashVerification(false, expected, null, null, "Artifact id does not resolve inside the store root."));
        }

        var entry = ReadEntry(Path.Combine(directory, EntryFileName));
        var contentPath = Path.Combine(directory, ContentFileName);

        if (!File.Exists(contentPath))
        {
            return Task.FromResult(new ArtifactHashVerification(false, expected, entry?.ContentDigest, null, "The stored content is missing."));
        }

        ArtifactDigest recomputed;
        using (var stream = File.OpenRead(contentPath))
        {
            recomputed = ArtifactDigest.Compute(stream);
        }

        var matches = recomputed == expected;
        var detail = matches
            ? (entry is not null && entry.ContentDigest == recomputed
                ? "Stored bytes match both the expected and the recorded digest."
                : "Stored bytes match the expected digest, but the recorded digest differs — the entry is inconsistent.")
            : $"Stored bytes hash to {recomputed}, not {expected}.";

        return Task.FromResult(new ArtifactHashVerification(matches, expected, entry?.ContentDigest, recomputed, detail));
    }

    public Task<ArtifactStoreEntry?> ResolveByArtifactIdAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveArtifactDirectory(artifactId, out var directory))
        {
            return Task.FromResult<ArtifactStoreEntry?>(null);
        }

        return Task.FromResult(ReadEntry(Path.Combine(directory, EntryFileName)));
    }

    public Task<IReadOnlyList<ArtifactStoreEntry>> ResolveByBuildIdAsync(BuildId buildId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buildId);
        cancellationToken.ThrowIfCancellationRequested();

        var indexPath = BuildIndexPath(buildId);
        if (!File.Exists(indexPath))
        {
            return Task.FromResult<IReadOnlyList<ArtifactStoreEntry>>([]);
        }

        var entries = new List<ArtifactStoreEntry>();

        foreach (var line in File.ReadAllLines(indexPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = TryReadEntryByValue(line);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        // Ordered by artifact id so the result is identical across adapters and run orders.
        return Task.FromResult<IReadOnlyList<ArtifactStoreEntry>>(
            [.. entries.OrderBy(e => e.ArtifactId.Value, StringComparer.Ordinal)]);
    }

    public Task<ArtifactStoreEntry> SetLifecycleAsync(
        ArtifactId artifactId,
        ArtifactLifecycleState state,
        string reason,
        ArtifactId? supersededBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A lifecycle change requires a reason. 'Retired' and 'vanished' look the same without one, and a quarantine nobody can review is hard to lift.",
                nameof(reason));
        }

        if (!TryResolveArtifactDirectory(artifactId, out var directory))
        {
            throw new InvalidOperationException($"Artifact '{artifactId}' does not resolve inside the store root.");
        }

        lock (_gate)
        {
            var entryPath = Path.Combine(directory, EntryFileName);
            var entry = ReadEntry(entryPath)
                ?? throw new InvalidOperationException($"Artifact '{artifactId}' is not published, so its lifecycle cannot be changed.");

            var updated = entry with
            {
                Lifecycle = state,
                LifecycleReason = reason,
                SupersededBy = supersededBy
            };

            File.WriteAllText(entryPath, JsonSerializer.Serialize(EntryDto.From(updated), Options), Encoding.UTF8);

            return Task.FromResult(updated);
        }
    }

    private static ArtifactStoreEntry BuildEntry(PackagedArtifact artifact, ArtifactDigest actual, long size)
        => new(
            artifact.ArtifactId,
            artifact.BuildId,
            actual,
            size,
            DateTimeOffset.UtcNow,
            ArtifactLifecycleState.Published,
            null,
            null,
            artifact.FileName);

    private void AppendToBuildIndex(BuildId buildId, ArtifactId artifactId)
    {
        var buildsDirectory = Path.Combine(_root, BuildsDirectoryName);
        Directory.CreateDirectory(buildsDirectory);

        var indexPath = BuildIndexPath(buildId);
        var existing = File.Exists(indexPath) ? File.ReadAllLines(indexPath) : [];

        if (existing.Contains(artifactId.Value, StringComparer.Ordinal))
        {
            return;
        }

        File.AppendAllText(indexPath, artifactId.Value + Environment.NewLine, Encoding.UTF8);
    }

    private string BuildIndexPath(BuildId buildId) => Path.Combine(_root, BuildsDirectoryName, buildId.Value + ".index");

    /// <summary>
    /// Materialises an identity binding for <b>every artifact the store already holds</b>, reading each one's
    /// own <c>entry.json</c>.
    ///
    /// <para>
    /// <b>Why this is needed and why it is explicit.</b> Bindings are written when an artifact is published,
    /// so every artifact that entered a store before W9.6 has none — which is every artifact in this estate,
    /// including the one W9.6 is a pre-gate for. Without this, the old artifacts would be protected only from
    /// the day they were next re-published, and the W9.5 incident could simply be repeated against them.
    /// </para>
    ///
    /// <para>
    /// It is a separate, explicitly invoked operation rather than something the constructor does, because a
    /// store that silently repaired its own control records on open would be a store whose guarantees depend
    /// on nobody having looked. Running it is an act, and the returned list is its evidence.
    /// </para>
    ///
    /// <para>
    /// It cannot change what an id means: the digest is read from the artifact's own entry, so a binding can
    /// only ever be created for the payload that is actually there, and an existing binding is never
    /// rewritten.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<ArtifactIdentity>> EnsureIdentityBindingsAsync(CancellationToken cancellationToken = default)
    {
        var created = new List<ArtifactIdentity>();
        var artifactsDirectory = Path.Combine(_root, ArtifactsDirectoryName);

        if (!Directory.Exists(artifactsDirectory))
        {
            return Task.FromResult<IReadOnlyList<ArtifactIdentity>>(created);
        }

        lock (_gate)
        {
            foreach (var entryPath in Directory.EnumerateFiles(artifactsDirectory, EntryFileName, SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = ReadEntry(entryPath);

                if (entry is null || File.Exists(IdentityPath(entry.ArtifactId)))
                {
                    continue;
                }

                var identity = new ArtifactIdentity
                {
                    SchemaVersion = ArtifactIdentity.CurrentSchema,
                    ArtifactId = entry.ArtifactId,
                    ContentDigest = entry.ContentDigest,
                    BuildId = entry.BuildId,
                    FirstAcceptedUtc = entry.PublishedAt,
                    State = ArtifactIdentityState.Active,
                    PublishAttempts = 1
                };

                WriteIdentity(identity);
                created.Add(identity);
            }
        }

        return Task.FromResult<IReadOnlyList<ArtifactIdentity>>(created);
    }

    // ------------------------------------------------------------------ identity bindings

    /// <summary>
    /// Where one id's binding lives. Hashed rather than path-shaped, because an artifact id contains slashes
    /// and an <c>@</c> and must not be able to steer the write anywhere it likes.
    /// </summary>
    private string IdentityPath(ArtifactId artifactId)
    {
        var name = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(artifactId.Value)));

        return Path.Combine(_root, IdentitiesDirectoryName, name + ".json");
    }

    /// <summary>
    /// The identity binding as it is stored. Strings, for the same reason <see cref="EntryDto"/> uses them:
    /// the contract types are parse-only by design — an <see cref="ArtifactId"/> cannot be constructed from
    /// arbitrary JSON, only parsed from its own grammar — so the file format carries the canonical string and
    /// the reader re-parses it. Re-parsing is the point rather than a workaround: a value read back out of a
    /// store is not trusted, it is re-validated.
    /// </summary>
    private sealed record IdentityDto(
        [property: JsonPropertyOrder(0)] string SchemaVersion,
        [property: JsonPropertyOrder(1)] string ArtifactId,
        [property: JsonPropertyOrder(2)] string ContentDigest,
        [property: JsonPropertyOrder(3)] string BuildId,
        [property: JsonPropertyOrder(4)] string FirstAcceptedUtc,
        [property: JsonPropertyOrder(5)] string State,
        [property: JsonPropertyOrder(6)] int PublishAttempts,
        [property: JsonPropertyOrder(7)] string? WithdrawnReason,
        [property: JsonPropertyOrder(8)] string? WithdrawnBy,
        [property: JsonPropertyOrder(9)] string? WithdrawnUtc)
    {
        internal static IdentityDto From(ArtifactIdentity identity) => new(
            identity.SchemaVersion,
            identity.ArtifactId.Value,
            identity.ContentDigest.ToString(),
            identity.BuildId.Value,
            identity.FirstAcceptedUtc.ToString("O"),
            identity.State.ToString(),
            identity.PublishAttempts,
            identity.WithdrawnReason,
            identity.WithdrawnBy,
            identity.WithdrawnUtc?.ToString("O"));

        internal ArtifactIdentity ToIdentity() => new()
        {
            SchemaVersion = SchemaVersion,
            ArtifactId = Contracts.ArtifactId.Parse(ArtifactId),
            ContentDigest = Contracts.ArtifactDigest.Parse(ContentDigest),
            BuildId = Contracts.BuildId.Parse(BuildId),
            FirstAcceptedUtc = DateTimeOffset.Parse(FirstAcceptedUtc, System.Globalization.CultureInfo.InvariantCulture),
            State = Enum.Parse<ArtifactIdentityState>(State),
            PublishAttempts = PublishAttempts,
            WithdrawnReason = WithdrawnReason,
            WithdrawnBy = WithdrawnBy,
            WithdrawnUtc = WithdrawnUtc is null
                ? null
                : DateTimeOffset.Parse(WithdrawnUtc, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private ArtifactIdentity? ReadIdentity(ArtifactId artifactId)
    {
        var path = IdentityPath(artifactId);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IdentityDto>(File.ReadAllText(path), Options)?.ToIdentity();
        }
        catch (JsonException)
        {
            // A binding that cannot be read is NOT treated as absent. Absent means "never used"; unreadable
            // means "something is wrong with the only record that can answer that", and returning null would
            // turn the second into the first - which is the defect this whole type exists to close.
            throw new InvalidOperationException(
                $"The identity binding for '{artifactId}' exists but could not be read. It is not treated as "
                + "absent: an unreadable binding is not evidence that the id was never used.");
        }
    }

    private void WriteIdentity(ArtifactIdentity identity)
    {
        var directory = Path.Combine(_root, IdentitiesDirectoryName);
        Directory.CreateDirectory(directory);

        var path = IdentityPath(identity.ArtifactId);
        var temporary = path + ".staging";

        File.WriteAllText(temporary, JsonSerializer.Serialize(IdentityDto.From(identity), Options), Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Appends one line to the store's publish-attempt log. <b>Append-only, and never consulted by a
    /// decision</b>: this is observability, not a control, and the binding above is the control. A log that
    /// decisions read would become a second authority on whether an id is taken.
    /// </summary>
    private void RecordAttempt(ArtifactId artifactId, ArtifactDigest digest, bool accepted, string outcome)
    {
        try
        {
            var directory = Path.Combine(_root, IdentitiesDirectoryName);
            Directory.CreateDirectory(directory);

            var line = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                artifactId = artifactId.Value,
                digest = digest.ToString(),
                accepted,
                outcome
            }, Options);

            File.AppendAllText(Path.Combine(directory, "publish-attempts.jsonl"), line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is not a control. Failing a publish because its observability line could not be written
            // would make the log load-bearing, which is the opposite of what it is for.
        }
    }

    /// <summary>
    /// <b>The one governed destructive act this store supports.</b> Marks the identity withdrawn and, when
    /// asked, removes the payload — never the binding.
    ///
    /// <para>
    /// This exists because the alternative people reach for is <c>rm -rf</c> on the artifact directory, and
    /// that destroys the only record that the id was used. Withdrawal records the same intent in a form the
    /// store can still read afterwards, so the next publish under that id is refused by mechanism rather than
    /// by somebody remembering. It does not delete the bytes by default: the payload is evidence until
    /// somebody states that it is not.
    /// </para>
    /// </summary>
    /// <param name="artifactId">The id to withdraw. Must already be bound.</param>
    /// <param name="reason">Why. Required — an unexplained withdrawal is indistinguishable from an accident.</param>
    /// <param name="withdrawnBy">The lane or actor withdrawing it.</param>
    /// <param name="removePayload">Whether to also remove the bytes. The identity survives either way.</param>
    public Task<ArtifactWithdrawalOutcome> WithdrawAsync(
        ArtifactId artifactId,
        string reason,
        string withdrawnBy,
        bool removePayload = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(withdrawnBy))
        {
            return Task.FromResult(ArtifactWithdrawalOutcome.Refused(
                ArtifactIdentityRefusalReason.WithdrawalNotPermitted,
                "A withdrawal must state a reason and an actor. An unexplained withdrawal is indistinguishable "
                + "from an accident, and the next reader cannot tell whether the payload was wrong or the "
                + "intention changed."));
        }

        lock (_gate)
        {
            var identity = ReadIdentity(artifactId);

            if (identity is null)
            {
                return Task.FromResult(ArtifactWithdrawalOutcome.Refused(
                    ArtifactIdentityRefusalReason.WithdrawalNotPermitted,
                    $"'{artifactId}' has no identity binding, so there is nothing to withdraw. An id that was "
                    + "never published is not withdrawn - it is simply unused."));
            }

            if (identity.IsWithdrawn)
            {
                return Task.FromResult(ArtifactWithdrawalOutcome.Refused(
                    ArtifactIdentityRefusalReason.ArtifactIdWithdrawn,
                    $"'{artifactId}' was already withdrawn at {identity.WithdrawnUtc:O} by '{identity.WithdrawnBy}': "
                    + $"{identity.WithdrawnReason}."));
            }

            var removed = false;

            if (removePayload && TryResolveArtifactDirectory(artifactId, out var directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
                removed = true;
            }

            WriteIdentity(identity with
            {
                State = ArtifactIdentityState.Withdrawn,
                WithdrawnReason = reason,
                WithdrawnBy = withdrawnBy,
                WithdrawnUtc = DateTimeOffset.UtcNow
            });

            RecordAttempt(artifactId, identity.ContentDigest, accepted: false, "withdrawn");

            return Task.FromResult(ArtifactWithdrawalOutcome.Withdrawn(
                removed,
                $"'{artifactId}' withdrawn by '{withdrawnBy}': {reason}. "
                + (removed ? "The payload was removed. " : "The payload was kept. ")
                + "The id is now permanently spent; publish a new version."));
        }
    }

    private ArtifactStoreEntry? TryReadEntryByValue(string artifactIdValue)
    {
        // The id is read back from an index this store wrote, so it is re-parsed rather than trusted: an
        // index line that no longer parses is skipped instead of aborting the whole lookup. The parse
        // itself lives on ArtifactId, so this store and every other reader of a stored id agree by
        // construction rather than by keeping two switches in step.
        return ArtifactId.TryParse(artifactIdValue, out var artifactId) && artifactId is not null
            ? ReadEntry(Path.Combine(ArtifactDirectory(artifactId), EntryFileName))
            : null;
    }

    private ArtifactStoreEntry? ReadEntry(string entryPath)
    {
        if (!File.Exists(entryPath))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<EntryDto>(File.ReadAllText(entryPath), Options);
            return dto?.ToEntry();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool TryResolveArtifactDirectory(ArtifactId artifactId, out string directory)
    {
        directory = string.Empty;

        if (artifactId is null || string.IsNullOrWhiteSpace(artifactId.Value))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, ArtifactsDirectoryName, artifactId.Value.Replace('/', Path.DirectorySeparatorChar)));
        var expectedPrefix = Path.Combine(_root, ArtifactsDirectoryName) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        directory = candidate;
        return true;
    }

    private string ArtifactDirectory(ArtifactId artifactId)
        => Path.Combine(_root, ArtifactsDirectoryName, artifactId.Value.Replace('/', Path.DirectorySeparatorChar));

    private sealed record EntryDto(
        [property: JsonPropertyOrder(0)] string ArtifactId,
        [property: JsonPropertyOrder(1)] string BuildId,
        [property: JsonPropertyOrder(2)] string ContentDigest,
        [property: JsonPropertyOrder(3)] long SizeBytes,
        [property: JsonPropertyOrder(4)] string PublishedAt,
        [property: JsonPropertyOrder(5)] string Lifecycle,
        [property: JsonPropertyOrder(6)] string? LifecycleReason,
        [property: JsonPropertyOrder(7)] string? SupersededBy,
        [property: JsonPropertyOrder(8)] string? FileName)
    {
        internal static EntryDto From(ArtifactStoreEntry entry) => new(
            entry.ArtifactId.Value,
            entry.BuildId.Value,
            entry.ContentDigest.ToString(),
            entry.SizeBytes,
            entry.PublishedAt.ToString("O"),
            entry.Lifecycle.ToString(),
            entry.LifecycleReason,
            entry.SupersededBy?.Value,
            entry.FileName);

        internal ArtifactStoreEntry ToEntry() => new(
            Contracts.ArtifactId.Parse(ArtifactId),
            Contracts.BuildId.Parse(BuildId),
            Contracts.ArtifactDigest.Parse(ContentDigest),
            SizeBytes,
            DateTimeOffset.Parse(PublishedAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            Enum.Parse<ArtifactLifecycleState>(Lifecycle),
            LifecycleReason,
            SupersededBy is null ? null : Contracts.ArtifactId.Parse(SupersededBy),
            FileName);
    }
}
