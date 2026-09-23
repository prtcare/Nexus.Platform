using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The first <see cref="IReleaseLineageLog"/> adapter: an append-only, local, file-backed ledger.
///
/// <para>
/// <b>Append-only means append-only.</b> There is no update and no delete, because a ledger that can be
/// edited is a ledger that cannot be used as evidence. A record written in error is superseded by a later
/// record, never corrected in place — and the estate's doctrine is stronger than that: never re-run a
/// scenario that has written an immutable record, and restore a pre-run backup instead.
/// </para>
///
/// <para>
/// Layout under the configured root: one file per record, named for its lineage id, so an append is a
/// create and a duplicate is a refusal rather than a silent second line.
/// <code>
/// &lt;lineageId&gt;.json    one release lineage record
/// </code>
/// </para>
/// </summary>
public sealed class FileReleaseLineageLog : IReleaseLineageLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _root;
    private readonly object _gate = new();

    public FileReleaseLineageLog(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A release lineage log needs a root directory.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public string Root => _root;

    public async Task AppendAsync(ReleaseLineageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, record.LineageId + ".json");

        Task? writeTask;

        lock (_gate)
        {
            // A duplicate lineage id and a second record for the same release are both refusals. The second
            // is the subtler one: a release occupies one position in the chain, and a second row claiming
            // that position is a correction attempt wearing an append's clothing.
            foreach (var existing in ReadAll())
            {
                if (string.Equals(existing.LineageId, record.LineageId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Lineage id '{record.LineageId}' is already recorded. A lineage id identifies one act and is never reused.");
                }

                if (existing.ReleaseId == record.ReleaseId)
                {
                    throw new InvalidOperationException(
                        $"Release '{record.ReleaseId}' is already recorded as '{existing.LineageId}'. A release occupies one position in the chain; supersede it with a new release rather than appending a second row.");
                }
            }

            writeTask = File.WriteAllTextAsync(path, JsonSerializer.Serialize(RecordDto.From(record), Options), Encoding.UTF8, cancellationToken);
        }

        await writeTask.ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ReleaseLineageRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadAll());
    }

    public Task<ReleaseLineageRecord?> FindByReleaseIdAsync(ReleaseId releaseId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ReadAll().FirstOrDefault(r => r.ReleaseId == releaseId));
    }

    private IReadOnlyList<ReleaseLineageRecord> ReadAll()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var records = new List<ReleaseLineageRecord>();

        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<RecordDto>(File.ReadAllText(file), Options);
                var record = dto?.ToRecord();
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                // A file that no longer parses is skipped rather than aborting the whole read. The ledger is
                // append-only, so a malformed row is a damage finding and not a reason to hide the rest.
            }
        }

        // Ordered by lineage id: numeric by its suffix rather than lexically, so L-W9-10 follows L-W9-9.
        return [.. records.OrderBy(r => LineageOrdinal(r.LineageId))];
    }

    private static int LineageOrdinal(string lineageId)
        => int.TryParse(lineageId[ReleaseLineageRecord.SeriesPrefix.Length..], out var value) ? value : int.MaxValue;

    private sealed record RecordDto(
        [property: JsonPropertyOrder(0)] string LineageId,
        [property: JsonPropertyOrder(1)] string OccurredAt,
        [property: JsonPropertyOrder(2)] string ReleaseId,
        [property: JsonPropertyOrder(3)] string UnitId,
        [property: JsonPropertyOrder(4)] string Version,
        [property: JsonPropertyOrder(5)] string BuildId,
        [property: JsonPropertyOrder(6)] string BundleId,
        [property: JsonPropertyOrder(7)] string ReleaseRefName,
        [property: JsonPropertyOrder(8)] string[] SourceCommits,
        [property: JsonPropertyOrder(9)] ArtifactDto[] Artifacts,
        [property: JsonPropertyOrder(10)] string? OriginatingWork,
        [property: JsonPropertyOrder(11)] string? OriginatingWorkTitle,
        [property: JsonPropertyOrder(12)] string? OriginatingWorkSeries,
        [property: JsonPropertyOrder(13)] string? BuildManifestReference,
        [property: JsonPropertyOrder(14)] string? Reason)
    {
        internal static RecordDto From(ReleaseLineageRecord record) => new(
            record.LineageId,
            record.OccurredAt.ToString("O"),
            record.ReleaseId.Value,
            record.UnitId.Value,
            record.Version,
            record.BuildId.Value,
            record.BundleId.Value,
            record.ReleaseRefName,
            [.. record.SourceCommits],
            [.. record.Artifacts.Select(a => new ArtifactDto(a.ArtifactId.Value, a.ContentDigest.ToString(), a.SizeBytes))],
            record.OriginatingWork?.WorkReference,
            record.OriginatingWork?.Title,
            record.OriginatingWork?.Series,
            record.BuildManifestReference,
            record.Reason);

        internal ReleaseLineageRecord ToRecord() => new(
            LineageId,
            DateTimeOffset.Parse(OccurredAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            Contracts.ReleaseId.Parse(ReleaseId),
            Contracts.DeploymentUnitId.Parse(UnitId),
            Version,
            Contracts.BuildId.Parse(BuildId),
            Contracts.BundleId.Parse(BundleId),
            ReleaseRefName,
            SourceCommits,
            [.. Artifacts.Select(a => new ReleaseArtifactIdentity(
                Contracts.ArtifactId.Parse(a.ArtifactId),
                Contracts.ArtifactDigest.Parse(a.Digest),
                a.SizeBytes))],
            OriginatingWork is null ? null : new GovernedWorkReference(OriginatingWork, OriginatingWorkTitle, OriginatingWorkSeries),
            BuildManifestReference,
            Reason);
    }

    private sealed record ArtifactDto(
        [property: JsonPropertyOrder(0)] string ArtifactId,
        [property: JsonPropertyOrder(1)] string Digest,
        [property: JsonPropertyOrder(2)] long SizeBytes);
}
