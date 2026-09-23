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

                return Task.FromResult(ArtifactPublishOutcome.Accepted(entry, alreadyPresent: false));
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
