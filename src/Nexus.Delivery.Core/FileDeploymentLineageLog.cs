using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The first <see cref="IDeploymentLineageLog"/> adapter: an append-only, file-backed ledger.
///
/// <para>
/// <b>Append-only by construction.</b> The file is opened in <see cref="FileMode.Append"/> and there
/// is no update or delete path in this type at all. A correction is a new record naming the one it
/// supersedes — invariant L-3 — and the signature makes the alternative unavailable rather than
/// discouraged.
/// </para>
///
/// <para>
/// <b>Duplicates are refused on immutable record identity</b>, the same discipline W8E's workbook
/// append uses. That refusal is load-bearing rather than defensive: it is what makes a re-run of a
/// recording step fail loudly instead of writing a second, contradictory history.
/// </para>
///
/// <para>
/// <b>This is the proof-grade adapter, not the governed one.</b> The governed writer — into the
/// authority workbook's <c>13_GitLineage</c> sheet, through the shared DevelopmentControl component,
/// under a reservation, with the authority pin re-certified afterwards — is a W9.2 deliverable, and
/// <c>DEPLOYMENT_LINEAGE_MODEL.md</c> §1 requires the reader/writer state to be re-verified before
/// that work begins rather than assumed green from memory.
/// </para>
/// </summary>
public sealed class FileDeploymentLineageLog : IDeploymentLineageLog
{
    private const string FileName = "lineage.jsonl";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly object _gate = new();

    public FileDeploymentLineageLog(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A lineage log needs a root directory.", nameof(rootDirectory));
        }

        var root = System.IO.Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        _path = System.IO.Path.Combine(root, FileName);
    }

    /// <summary>
    /// The ledger file. Deliberately NOT named <c>Path</c>: a member of that name shadows
    /// <see cref="System.IO.Path"/> throughout the class body, which turns every unqualified path call
    /// into a compile error and every qualified one into noise.
    /// </summary>
    public string LogPath => _path;

    public Task<LineageAppendOutcome> AppendAsync(DeploymentLineageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var line = JsonSerializer.Serialize(LineageDto.From(record), Options);

        lock (_gate)
        {
            foreach (var existing in ReadLines())
            {
                if (existing.LineageId == record.LineageId)
                {
                    return Task.FromResult(LineageAppendOutcome.Refused(
                        $"Lineage record '{record.LineageId}' already exists. Records are append-only and are corrected by supersession, never by rewrite."));
                }
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
        }

        return Task.FromResult(LineageAppendOutcome.Appended());
    }

    public Task<IReadOnlyList<DeploymentLineageRecord>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DeploymentLineageRecord>>([.. ReadLines().Select(d => d.ToRecord())]);
        }
    }

    public async Task<DeploymentLineageRecord?> TryGetAsync(string lineageId, CancellationToken cancellationToken = default)
    {
        var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(r => string.Equals(r.LineageId, lineageId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<DeploymentLineageRecord>> ReadByBundleAsync(BundleId bundleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleId);

        var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);

        return [.. all
            .Where(r => r.BundleId == bundleId || r.PreviousBundleId == bundleId)
            .OrderBy(r => r.OccurredAt)];
    }

    private List<LineageDto> ReadLines()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var records = new List<LineageDto>();

        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var dto = JsonSerializer.Deserialize<LineageDto>(line, Options);
                if (dto is not null)
                {
                    records.Add(dto);
                }
            }
            catch (JsonException)
            {
                // A corrupt line does not hide the rest of the ledger. The record is skipped rather
                // than the read failing, because an unreadable ledger would be indistinguishable from
                // an empty one — and "no deployments recorded" is a much worse answer than a gap.
            }
        }

        return records;
    }

    private sealed record LineageDto(
        [property: JsonPropertyOrder(0)] string LineageId,
        [property: JsonPropertyOrder(1)] string OccurredAt,
        [property: JsonPropertyOrder(2)] string Transition,
        [property: JsonPropertyOrder(3)] string BundleId,
        [property: JsonPropertyOrder(4)] string FromState,
        [property: JsonPropertyOrder(5)] string ToState,
        [property: JsonPropertyOrder(6)] string Environment,
        [property: JsonPropertyOrder(7)] string SourceCommit,
        [property: JsonPropertyOrder(8)] ArtifactDto[] Artifacts,
        [property: JsonPropertyOrder(9)] string MigrationProvider,
        [property: JsonPropertyOrder(10)] string MigrationSetDigest,
        [property: JsonPropertyOrder(11)] string[]? MigrationIds,
        [property: JsonPropertyOrder(12)] string? AuthorizedBy,
        [property: JsonPropertyOrder(13)] string? AuthorizationRole,
        [property: JsonPropertyOrder(14)] string? Reason,
        [property: JsonPropertyOrder(15)] string? PreviousBundleId,
        [property: JsonPropertyOrder(16)] string[]? ConfigurationKeys,
        [property: JsonPropertyOrder(17)] string[]? SecretReferences,
        [property: JsonPropertyOrder(18)] bool? RuntimeConsumptionObserved)
    {
        internal static LineageDto From(DeploymentLineageRecord record) => new(
            record.LineageId,
            record.OccurredAt.ToString("O"),
            record.Transition.ToString(),
            record.BundleId.Value,
            record.FromState.ToString(),
            record.ToState.ToString(),
            record.Environment.Value,
            record.SourceCommitSha,
            [.. record.Artifacts.Select(a => new ArtifactDto(a.UnitId.Value, a.Digest.ToString(), a.SizeBytes))],
            record.Migrations.Provider,
            record.Migrations.SetDigest.ToString(),
            [.. record.Migrations.MigrationIds],
            record.Authorization?.ActorIdentity,
            record.Authorization?.Role.ToString(),
            record.Reason,
            record.PreviousBundleId?.Value,
            [.. record.ConfigurationKeys],
            [.. record.SecretReferences.Select(s => s.Value)],
            record.RuntimeConsumptionObserved);

        internal DeploymentLineageRecord ToRecord() => new(
            LineageId,
            DateTimeOffset.Parse(OccurredAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            Enum.Parse<DeploymentTransition>(Transition),
            Contracts.BundleId.Parse(BundleId),
            Enum.Parse<PromotionState>(FromState),
            Enum.Parse<PromotionState>(ToState),
            Contracts.DeploymentEnvironmentId.Parse(Environment),
            SourceCommit,
            [.. Artifacts.Select(a => new ReleaseArtifact(
                Contracts.DeploymentUnitId.Parse(a.UnitId),
                Contracts.ArtifactDigest.Parse(a.Digest),
                a.SizeBytes))],
            new MigrationMetadata(MigrationProvider, MigrationIds ?? []),
            AuthorizationRole is null || AuthorizedBy is null
                ? null
                : new DeploymentAuthorization(Enum.Parse<DeploymentAuthorityRole>(AuthorizationRole), AuthorizedBy, default),
            Reason,
            PreviousBundleId is null ? null : Contracts.BundleId.Parse(PreviousBundleId),
            ConfigurationKeys,
            [.. (SecretReferences ?? []).Select(Contracts.SecretReference.Parse)],
            RuntimeConsumptionObserved);
    }

    private sealed record ArtifactDto(
        [property: JsonPropertyOrder(0)] string UnitId,
        [property: JsonPropertyOrder(1)] string Digest,
        [property: JsonPropertyOrder(2)] long SizeBytes);
}
