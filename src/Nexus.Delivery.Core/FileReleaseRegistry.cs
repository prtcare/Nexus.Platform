using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The first <see cref="IReleaseRegistry"/> adapter: a deterministic, local, file-backed release registry.
///
/// <para>
/// <b>Same reasoning as the two adapters beneath it: the least capable thing that satisfies the contract.</b>
/// Nothing about a filesystem may bias the interface, and if it does, that is discovered here for the cost
/// of a test rather than when an object-storage or OCI adapter is written.
/// </para>
///
/// <para>
/// <b>Immutability is enforced, not assumed.</b> Registering a <see cref="ReleaseId"/> whose record digest
/// differs from the one held is refused. The record is written with <see cref="FileMode.CreateNew"/> — an
/// OS-level atomic create — so two concurrent registrations of the same id cannot both succeed and no
/// caller has to check-then-write and hope nothing raced it.
/// </para>
///
/// <para>
/// <b>Why a re-registration of identical content is a no-op rather than a refusal.</b> The reasoning is
/// <see cref="ArtifactPublishOutcome.IsAlreadyPresent"/>'s, one level up. Re-assembling a release from
/// identical inputs changes nothing, so refusing would make a retried pipeline fail while adding no
/// protection. What must never be accepted is the same id with different content, and that is refused.
/// Note that this differs from W9.1's bundle registry, which refuses any second write; there the bundle id
/// is hand-chosen and a second write is ambiguous, while here the id is derived and a second write is
/// either provably identical or a governance failure.
/// </para>
///
/// <para>
/// Layout under the configured root, which is the artifact store root so that a release, its artifacts and
/// its build indexes live in one place:
/// <code>
/// releases/&lt;ReleaseId&gt;/release-bundle.json    the canonical record; its digest is the immutability key
/// releases/&lt;ReleaseId&gt;/entry.json             identity, digest and lifecycle
/// releases/&lt;ReleaseId&gt;/provenance.json        the build and bundle this release points at
/// </code>
/// </para>
/// </summary>
public sealed class FileReleaseRegistry : IReleaseRegistry
{
    private const string ReleasesDirectoryName = "releases";
    private const string RecordFileName = "release-bundle.json";
    private const string EntryFileName = "entry.json";
    private const string ProvenanceFileName = "provenance.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _root;
    private readonly object _gate = new();

    public FileReleaseRegistry(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A release registry needs a root directory.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public string Root => _root;

    public Task<ReleaseRegistrationOutcome> RegisterAsync(ReleaseRecord release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveReleaseDirectory(release.ReleaseId, out var directory))
        {
            return Task.FromResult(ReleaseRegistrationOutcome.Refused(
                ReleaseRegistryRefusalReason.PathEscapesRegistryRoot,
                $"Release id '{release.ReleaseId}' does not resolve inside the registry root."));
        }

        var encoded = ReleaseRecordCodec.Encode(release);

        // The release's own digest, not a digest of its storage rendering. One definition, shared with the
        // release reference's governed annotation.
        var digest = release.ComputeRecordDigest();

        lock (_gate)
        {
            var entryPath = Path.Combine(directory, EntryFileName);
            var recordPath = Path.Combine(directory, RecordFileName);

            if (File.Exists(entryPath))
            {
                var existing = ReadEntry(entryPath);

                if (existing is not null && existing.RecordDigest != digest)
                {
                    return Task.FromResult(ReleaseRegistrationOutcome.Refused(
                        ReleaseRegistryRefusalReason.ReleaseIdExistsWithDifferentContent,
                        $"'{release.ReleaseId}' is already registered with record digest {existing.RecordDigest}, "
                        + $"and the release presented hashes to {digest}. Changed artifacts, changed hashes or changed "
                        + "release metadata always mean a NEW release, never an overwrite."));
                }

                // Identical content already present. Repair the record if it went missing, then report a
                // no-op — a retried pipeline should succeed, not fail for having already succeeded.
                if (!File.Exists(recordPath))
                {
                    File.WriteAllBytes(recordPath, encoded);
                }

                return Task.FromResult(ReleaseRegistrationOutcome.Accepted(
                    existing ?? BuildEntry(release, digest, ReleaseLifecycleState.ReleaseDraft),
                    alreadyPresent: true));
            }

            try
            {
                Directory.CreateDirectory(directory);

                // CreateNew is the write-once guarantee at the byte level.
                using (var destination = new FileStream(recordPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    destination.Write(encoded, 0, encoded.Length);
                }

                var entry = BuildEntry(release, digest, ReleaseLifecycleState.ReleaseDraft);
                File.WriteAllText(entryPath, JsonSerializer.Serialize(EntryDto.From(entry), Options), Encoding.UTF8);
                File.WriteAllText(
                    Path.Combine(directory, ProvenanceFileName),
                    JsonSerializer.Serialize(ProvenanceDto.From(release), Options),
                    Encoding.UTF8);

                return Task.FromResult(ReleaseRegistrationOutcome.Accepted(entry, alreadyPresent: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ReleaseRegistrationOutcome.Refused(
                    ReleaseRegistryRefusalReason.StorageFailure,
                    $"Writing the release failed: {ex.GetType().Name}."));
            }
        }
    }

    public Task<bool> ExistsAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadEntryFor(releaseId) is not null);

    public Task<ReleaseRecord?> TryOpenAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveReleaseDirectory(releaseId, out var directory))
        {
            return Task.FromResult<ReleaseRecord?>(null);
        }

        var recordPath = Path.Combine(directory, RecordFileName);
        if (!File.Exists(recordPath))
        {
            return Task.FromResult<ReleaseRecord?>(null);
        }

        return Task.FromResult(
            ReleaseRecordCodec.TryDecode(File.ReadAllBytes(recordPath), out var release) ? release : null);
    }

    public Task<ReleaseRegistryEntry?> TryGetEntryAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadEntryFor(releaseId));

    public Task<ReleaseVerification> VerifyAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = ReadEntryFor(releaseId);

        if (entry is null)
        {
            return Task.FromResult(new ReleaseVerification(
                false, releaseId, null, null, null, "No such release in the registry."));
        }

        if (!TryResolveReleaseDirectory(releaseId, out var directory))
        {
            return Task.FromResult(new ReleaseVerification(
                false, releaseId, entry.RecordDigest, null, entry.Lifecycle,
                "The release id does not resolve inside the registry root."));
        }

        var recordPath = Path.Combine(directory, RecordFileName);
        if (!File.Exists(recordPath))
        {
            return Task.FromResult(new ReleaseVerification(
                false, releaseId, entry.RecordDigest, null, entry.Lifecycle,
                "The release is registered but its record is missing from the registry."));
        }

        var bytes = File.ReadAllBytes(recordPath);

        // The record must decode before it can be judged, because the digest is over what a release MEANS
        // rather than over the bytes that happen to hold it. Re-encoding first and hashing that would make
        // the check a digest of a rendering rather than of the release.
        if (!ReleaseRecordCodec.TryDecode(bytes, out var decoded) || decoded is null)
        {
            return Task.FromResult(new ReleaseVerification(
                false, releaseId, entry.RecordDigest, null, entry.Lifecycle,
                "The stored record does not decode, so the release it describes cannot be established."));
        }

        var recomputed = decoded.ComputeRecordDigest();

        if (recomputed != entry.RecordDigest)
        {
            return Task.FromResult(new ReleaseVerification(
                false, releaseId, entry.RecordDigest, recomputed, entry.Lifecycle,
                $"The stored record describes a release hashing to {recomputed}, not the registered {entry.RecordDigest}. "
                + "The registry's own contents have changed, which is a corruption finding."));
        }

        // The digest matching is necessary but not sufficient: the record's embedded release id must still
        // agree with the identity beside it. A record that hashed correctly and decoded to a different
        // release would defeat the lookup key from the inside.
        if (decoded.ReleaseId != releaseId)
        {
            return Task.FromResult(new ReleaseVerification(
                false, releaseId, entry.RecordDigest, recomputed, entry.Lifecycle,
                $"The stored record decodes to {decoded.ReleaseId}, not {releaseId}."));
        }

        return Task.FromResult(new ReleaseVerification(
            true, releaseId, entry.RecordDigest, recomputed, entry.Lifecycle,
            "The stored record matches its registered digest and decodes to the release it is filed under."));
    }

    public Task<ReleaseRegistryEntry> SetLifecycleAsync(
        ReleaseId releaseId,
        ReleaseLifecycleState state,
        string reason,
        ReleaseId? supersededBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A lifecycle change requires a reason. 'Withdrawn' and 'vanished' look the same without one, and a state change nobody can review is one nobody can correct.",
                nameof(reason));
        }

        if (!TryResolveReleaseDirectory(releaseId, out var directory))
        {
            throw new InvalidOperationException($"Release '{releaseId}' does not resolve inside the registry root.");
        }

        lock (_gate)
        {
            var entryPath = Path.Combine(directory, EntryFileName);
            var entry = ReadEntry(entryPath)
                ?? throw new InvalidOperationException($"Release '{releaseId}' is not registered, so its lifecycle cannot be changed.");

            if (entry.Lifecycle == ReleaseLifecycleState.Withdrawn && state != ReleaseLifecycleState.Withdrawn)
            {
                // Owner-reserved means reserved: nothing re-opens a withdrawn release. A new release is the
                // remedy, and that is the point of an immutable identity.
                throw new InvalidOperationException(
                    $"Release '{releaseId}' was withdrawn. Withdrawal is terminal; produce a new release instead.");
            }

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

    public Task<IReadOnlyList<ReleaseRegistryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var releasesRoot = Path.Combine(_root, ReleasesDirectoryName);
        if (!Directory.Exists(releasesRoot))
        {
            return Task.FromResult<IReadOnlyList<ReleaseRegistryEntry>>([]);
        }

        var entries = new List<ReleaseRegistryEntry>();

        foreach (var directory in Directory.EnumerateDirectories(releasesRoot))
        {
            var name = Path.GetFileName(directory);
            if (!ReleaseId.IsValid(name))
            {
                continue;
            }

            var entry = ReadEntry(Path.Combine(directory, EntryFileName));
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        // Ordered by release id so the result is identical across adapters and run orders.
        return Task.FromResult<IReadOnlyList<ReleaseRegistryEntry>>(
            [.. entries.OrderBy(e => e.ReleaseId.Value, StringComparer.Ordinal)]);
    }

    private static ReleaseRegistryEntry BuildEntry(ReleaseRecord release, ArtifactDigest digest, ReleaseLifecycleState lifecycle)
        => new(
            release.ReleaseId,
            digest,
            lifecycle,
            DateTimeOffset.UtcNow,
            release.UnitId,
            release.Version,
            release.BuildId,
            release.BundleId,
            release.Identity.ReleaseRefName);

    private ReleaseRegistryEntry? ReadEntryFor(ReleaseId releaseId)
    {
        ArgumentNullException.ThrowIfNull(releaseId);

        return TryResolveReleaseDirectory(releaseId, out var directory)
            ? ReadEntry(Path.Combine(directory, EntryFileName))
            : null;
    }

    private ReleaseRegistryEntry? ReadEntry(string entryPath)
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

    private bool TryResolveReleaseDirectory(ReleaseId releaseId, out string directory)
    {
        directory = string.Empty;

        if (releaseId is null || !ReleaseId.IsValid(releaseId.Value))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, ReleasesDirectoryName, releaseId.Value));
        var expectedPrefix = Path.Combine(_root, ReleasesDirectoryName) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        directory = candidate;
        return true;
    }

    private sealed record EntryDto(
        [property: JsonPropertyOrder(0)] string ReleaseId,
        [property: JsonPropertyOrder(1)] string RecordDigest,
        [property: JsonPropertyOrder(2)] string Lifecycle,
        [property: JsonPropertyOrder(3)] string RegisteredAt,
        [property: JsonPropertyOrder(4)] string UnitId,
        [property: JsonPropertyOrder(5)] string Version,
        [property: JsonPropertyOrder(6)] string BuildId,
        [property: JsonPropertyOrder(7)] string BundleId,
        [property: JsonPropertyOrder(8)] string ReleaseRefName,
        [property: JsonPropertyOrder(9)] string? LifecycleReason,
        [property: JsonPropertyOrder(10)] string? SupersededBy,
        [property: JsonPropertyOrder(11)] string? OwnerRef)
    {
        internal static EntryDto From(ReleaseRegistryEntry entry) => new(
            entry.ReleaseId.Value,
            entry.RecordDigest.ToString(),
            entry.Lifecycle.ToString(),
            entry.RegisteredAt.ToString("O"),
            entry.UnitId.Value,
            entry.Version,
            entry.BuildId.Value,
            entry.BundleId.Value,
            entry.ReleaseRefName,
            entry.LifecycleReason,
            entry.SupersededBy?.Value,
            entry.OwnerRef);

        internal ReleaseRegistryEntry ToEntry() => new(
            Contracts.ReleaseId.Parse(ReleaseId),
            Contracts.ArtifactDigest.Parse(RecordDigest),
            Enum.Parse<ReleaseLifecycleState>(Lifecycle),
            DateTimeOffset.Parse(RegisteredAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            Contracts.DeploymentUnitId.Parse(UnitId),
            Version,
            Contracts.BuildId.Parse(BuildId),
            Contracts.BundleId.Parse(BundleId),
            ReleaseRefName,
            LifecycleReason,
            SupersededBy is null ? null : Contracts.ReleaseId.Parse(SupersededBy),
            OwnerRef);
    }

    /// <summary>
    /// The release's pointers to everything it does not itself contain. Written beside the record so that a
    /// reader can follow the chain without decoding the release bundle — and so that the provenance is
    /// inspectable even if the record's schema moves on.
    /// </summary>
    private sealed record ProvenanceDto(
        [property: JsonPropertyOrder(0)] string ReleaseId,
        [property: JsonPropertyOrder(1)] string BundleId,
        [property: JsonPropertyOrder(2)] string BuildId,
        [property: JsonPropertyOrder(3)] string BuildManifestDigest,
        [property: JsonPropertyOrder(4)] string BuildManifestReference,
        [property: JsonPropertyOrder(5)] string ReleaseRefName,
        [property: JsonPropertyOrder(6)] string[] Artifacts,
        [property: JsonPropertyOrder(7)] string[] SourceCommits,
        [property: JsonPropertyOrder(8)] string? OriginatingWork)
    {
        internal static ProvenanceDto From(ReleaseRecord release) => new(
            release.ReleaseId.Value,
            release.BundleId.Value,
            release.BuildId.Value,
            release.Evidence.BuildManifestDigest.ToString(),
            release.Evidence.BuildManifestReference,
            release.Identity.ReleaseRefName,
            [.. release.Artifacts.OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal).Select(a => a.ArtifactId.Value)],
            [.. release.Identity.SourceCommits],
            release.OriginatingWork?.WorkReference);
    }
}
